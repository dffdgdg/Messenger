using Android.Media;
using Concentus;
using Concentus.Enums;
using Core.Services.Call;
using Core.Services.Call.Abstractions;
using Shared.Enum;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mobile.Android.Services.Platform;

public sealed class AndroidCallAudioService : ICallAudioService
{
    private const int SampleRate = 48000;
    private const int Channels = 1;
    private const int FrameDurationMs = 20;
    private const int FrameSamples = SampleRate * FrameDurationMs / 1000;
    private const int MaxEncodedBytes = 4000;

    private const double VadFloorThreshold = 0.008;
    private const double VadSpeechMultiplier = 2.5;
    private const int VadSilenceHoldMs = 1200;
    private const int VadDebounceMs = 150;
    private double _noiseFloor = 0.02;
    private const double NoiseFloorAlpha = 0.005;

    private AudioRecord? _audioRecord;
    private AudioTrack? _audioTrack;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private Task? _playbackTask;

    private IOpusEncoder? _encoder;
    private IOpusDecoder? _decoder;

    private readonly short[] _captureBuffer = new short[FrameSamples * 4];
    private int _captureBufferCount;
    private readonly Lock _captureLock = new();

    private readonly ConcurrentDictionary<int, ConcurrentQueue<float[]>> _playbackQueues = new();
    private readonly ConcurrentQueue<float[]> _serverMixedPlaybackQueue = new();
    private readonly float[] _mixBuffer = new float[FrameSamples];

    private bool _isRunning;
    private bool _isMuted;
    private CallMode _mode = CallMode.PeerToPeer;
    private bool _disposed;
    private readonly Lock _stateLock = new();

    private readonly NoiseReducer _noiseReducer = new(FrameSamples);

    private bool _isSpeaking;
    private DateTime _lastVoiceDetectedAt = DateTime.MinValue;
    private DateTime _lastSpeakingChangeSentAt = DateTime.MinValue;

    public event Action<byte[], int>? OnEncodedFrame;
    public event Action<bool>? SpeakingStateChanged;

    public bool IsRunning => _isRunning;
    public bool IsMuted => _isMuted;
    public bool HasParticipant(int userId) => _playbackQueues.ContainsKey(userId);

    public bool NoiseSuppressionEnabled
    {
        get => _noiseReducer.IsEnabled;
        set => _noiseReducer.IsEnabled = value;
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_isRunning) return;
            try
            {
                InitCodecs();
                StartCapture();
                StartPlayback();
                _isRunning = true;
                Debug.WriteLine("[AndroidCallAudio] Started");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidCallAudio] Start failed: {ex.Message}");
                StopInternal();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            StopInternal();
            Debug.WriteLine("[AndroidCallAudio] Stopped");
        }
    }

    public void SetMuted(bool muted) => _isMuted = muted;

    public void SetMode(CallMode mode)
    {
        _mode = mode;
        ClearServerMixedQueue();
    }

    public void AddParticipant(int userId)
    {
        _playbackQueues.TryAdd(userId, new ConcurrentQueue<float[]>());
        Debug.WriteLine($"[AndroidCallAudio] Participant added: {userId}");
    }

    public void RemoveParticipant(int userId)
    {
        _playbackQueues.TryRemove(userId, out _);
        Debug.WriteLine($"[AndroidCallAudio] Participant removed: {userId}");
    }

    public void ReceiveEncodedAudio(int fromUserId, byte[] opusData, int length)
    {
        if (_decoder == null) return;

        if (!_playbackQueues.TryGetValue(fromUserId, out var queue))
        {
            queue = new ConcurrentQueue<float[]>();
            _playbackQueues[fromUserId] = queue;
        }

        try
        {
            var decoded = new float[FrameSamples];
            var n = _decoder.Decode(opusData.AsSpan(0, length), decoded.AsSpan(), FrameSamples);
            if (n > 0 && queue.Count < 10) queue.Enqueue(decoded);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidCallAudio] Decode error from {fromUserId}: {ex.Message}");
        }
    }

    public void ReceiveMixedAudio(byte[] opusData)
    {
        if (_decoder == null || opusData.Length == 0) return;
        try
        {
            var decoded = new float[FrameSamples];
            var n = _decoder.Decode(opusData.AsSpan(), decoded.AsSpan(), FrameSamples);
            if (n > 0 && _serverMixedPlaybackQueue.Count < 10)
                _serverMixedPlaybackQueue.Enqueue(decoded);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidCallAudio] Decode mixed error: {ex.Message}");
        }
    }

    private void StartCapture()
    {
        int bufferSize = Math.Max(
            AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, Encoding.Pcm16bit),
            FrameSamples * 4
        );

        _audioRecord = new AudioRecord(
            AudioSource.VoiceCommunication,
            SampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit,
            bufferSize
        );

        if (_audioRecord.State != State.Initialized)
            throw new InvalidOperationException("AudioRecord init failed");

        _audioRecord.StartRecording();

        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var readBuffer = new short[FrameSamples];

        while (!ct.IsCancellationRequested && _audioRecord?.RecordingState == RecordState.Recording)
        {
            if (_isMuted || _encoder == null)
            {
                Thread.Sleep(5);
                continue;
            }

            try
            {
                int read = _audioRecord.Read(readBuffer, 0, readBuffer.Length);
                if (read <= 0) continue;

                lock (_captureLock)
                {
                    int toCopy = Math.Min(read, _captureBuffer.Length - _captureBufferCount);
                    Array.Copy(readBuffer, 0, _captureBuffer, _captureBufferCount, toCopy);
                    _captureBufferCount += toCopy;

                    while (_captureBufferCount >= FrameSamples)
                    {
                        EncodeAndSend(_captureBuffer.AsSpan(0, FrameSamples));
                        _captureBufferCount -= FrameSamples;
                        if (_captureBufferCount > 0)
                            Array.Copy(_captureBuffer, FrameSamples,
                                _captureBuffer, 0, _captureBufferCount);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Debug.WriteLine($"[AndroidCallAudio] Capture error: {ex.Message}");
                break;
            }
        }
    }

    private void EncodeAndSend(ReadOnlySpan<short> pcmFrame)
    {
        if (_encoder == null) return;
        try
        {
            Span<short> mutable = stackalloc short[pcmFrame.Length];
            pcmFrame.CopyTo(mutable);

            ProcessVoiceActivity(mutable);

            bool hasSpeech = _noiseReducer.Process(mutable);
            if (!hasSpeech) return;

            Span<byte> encoded = stackalloc byte[MaxEncodedBytes];
            int encodedLength = _encoder.Encode(mutable, FrameSamples, encoded, MaxEncodedBytes);

            if (encodedLength > 0)
            {
                var buffer = new byte[encodedLength];
                encoded[..encodedLength].CopyTo(buffer);
                OnEncodedFrame?.Invoke(buffer, encodedLength);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidCallAudio] Encode error: {ex.Message}");
        }
    }

    private void ProcessVoiceActivity(ReadOnlySpan<short> samples)
    {
        var now = DateTime.UtcNow;
        double sumSq = 0;
        for (int i = 0; i < samples.Length; i++)
            sumSq += (double)samples[i] * samples[i];
        double rms = Math.Sqrt(sumSq / samples.Length) / short.MaxValue;

        if (!_isSpeaking)
            _noiseFloor = (_noiseFloor * (1 - NoiseFloorAlpha)) + (rms * NoiseFloorAlpha);

        double threshold = Math.Max(VadFloorThreshold, _noiseFloor * VadSpeechMultiplier);
        bool hasVoice = rms > threshold;
        if (hasVoice) _lastVoiceDetectedAt = now;

        bool shouldBeSpeaking =
            (now - _lastVoiceDetectedAt).TotalMilliseconds < VadSilenceHoldMs;

        if (shouldBeSpeaking == _isSpeaking) return;
        if ((now - _lastSpeakingChangeSentAt).TotalMilliseconds < VadDebounceMs) return;

        _isSpeaking = shouldBeSpeaking;
        _lastSpeakingChangeSentAt = now;
        SpeakingStateChanged?.Invoke(_isSpeaking);
    }

    private void StartPlayback()
    {
        int bufferSize = Math.Max(
            AudioTrack.GetMinBufferSize(SampleRate, ChannelOut.Mono, Encoding.Pcm16bit),
            FrameSamples * 4
        );

        var attributes = new AudioAttributes.Builder()
            ?.SetUsage(AudioUsageKind.VoiceCommunication)
            ?.SetContentType(AudioContentType.Speech)
            ?.Build();

        var format = new AudioFormat.Builder()
            ?.SetSampleRate(SampleRate)
            ?.SetEncoding(Encoding.Pcm16bit)
            ?.SetChannelMask(ChannelOut.Mono)
            ?.Build();

        _audioTrack = new AudioTrack.Builder()
            ?.SetAudioAttributes(attributes)
            ?.SetAudioFormat(format)
            ?.SetBufferSizeInBytes(bufferSize)
            ?.SetTransferMode(AudioTrackMode.Stream)
            ?.Build();

        if (_audioTrack.State != AudioTrackState.Initialized)
            throw new InvalidOperationException("AudioTrack init failed");

        _audioTrack.Play();
        _playbackTask = Task.Run(() => PlaybackLoop(_cts!.Token));
    }

    private void PlaybackLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _audioTrack?.State == AudioTrackState.Initialized)
        {
            if (!MixNextFrame())
            {
                Thread.Sleep(FrameDurationMs / 2);
                continue;
            }

            var shortData = ConvertToShort(_mixBuffer);
            _audioTrack.Write(shortData, 0, shortData.Length);

            Thread.Sleep(FrameDurationMs / 2);
        }
    }

    private bool MixNextFrame()
    {
        Array.Clear(_mixBuffer, 0, FrameSamples);
        bool hasAudio = false;

        if (_mode == CallMode.ServerMixed)
        {
            if (_serverMixedPlaybackQueue.TryDequeue(out var frame))
            {
                hasAudio = true;
                Array.Copy(frame, _mixBuffer, Math.Min(frame.Length, FrameSamples));
            }
        }
        else
        {
            foreach (var queue in _playbackQueues.Values)
            {
                if (!queue.TryDequeue(out var frame)) continue;
                hasAudio = true;
                int count = Math.Min(frame.Length, FrameSamples);
                for (int i = 0; i < count; i++) _mixBuffer[i] += frame[i];
            }
        }

        if (hasAudio)
            for (int i = 0; i < FrameSamples; i++)
                _mixBuffer[i] = Math.Clamp(_mixBuffer[i], -1.0f, 1.0f);

        return hasAudio;
    }

    private static short[] ConvertToShort(float[] floats)
    {
        var result = new short[floats.Length];
        for (int i = 0; i < floats.Length; i++)
            result[i] = (short)(Math.Clamp(floats[i], -1f, 0.9999695f) * 32767f);
        return result;
    }

    private void InitCodecs()
    {
        _encoder = OpusCodecFactory.CreateEncoder(
            SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = 32000;
        _encoder.Complexity = 2;
        _encoder.UseVBR = true;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        _decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
    }

    private void StopInternal()
    {
        _isRunning = false;
        _isSpeaking = false;
        _lastVoiceDetectedAt = DateTime.MinValue;

        _cts?.Cancel();

        try { _captureTask?.Wait(500); } catch { }
        try { _playbackTask?.Wait(500); } catch { }

        _cts?.Dispose();
        _cts = null;

        try
        {
            _audioRecord?.Stop();
            _audioRecord?.Release();
            _audioRecord?.Dispose();
        }
        catch { }
        _audioRecord = null;

        try
        {
            _audioTrack?.Stop();
            _audioTrack?.Flush();
            _audioTrack?.Release();
            _audioTrack?.Dispose();
        }
        catch { }
        _audioTrack = null;

        _playbackQueues.Clear();
        ClearServerMixedQueue();
        _captureBufferCount = 0;
    }

    private void ClearServerMixedQueue()
    {
        while (_serverMixedPlaybackQueue.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_stateLock) { StopInternal(); }
    }
}