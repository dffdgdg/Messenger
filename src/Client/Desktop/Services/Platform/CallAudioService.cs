using Concentus;
using Concentus.Enums;
using Core.Services.Call;
using Core.Services.Call.Abstractions;
using Core.Services.Media.Abstractions;
using Shared.Enum;
using Silk.NET.OpenAL;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Desktop.Shared.Services.Platform;

public sealed class CallAudioService : ICallAudioService
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

    private readonly IAudioCaptureDevice _capture;
    private readonly IAudioPlaybackDevice _playback;
    private readonly OpenAlLifetime _openAl;
    private readonly AL _al;

    private IOpusEncoder? _encoder;
    private IOpusDecoder? _decoder;

    private readonly short[] _captureBuffer = new short[FrameSamples * 4];
    private int _captureBufferCount;
    private readonly Lock _captureLock = new();

    private readonly ConcurrentDictionary<int, ConcurrentQueue<float[]>> _playbackQueues = new();
    private readonly ConcurrentQueue<float[]> _serverMixedPlaybackQueue = new();
    private readonly float[] _mixBuffer = new float[FrameSamples];

    private uint _playbackSource;
    private readonly uint[] _playbackRingBuffers = new uint[8];
    private bool _playbackReady;
    private CancellationTokenSource? _playbackCts;
    private Task? _playbackTask;

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

    public CallAudioService(
        IAudioCaptureDeviceFactory captureFactory,
        IAudioPlaybackDevice playback,
        OpenAlLifetime openAl)
    {
        _capture = captureFactory.Create();
        _playback = playback;
        _openAl = openAl;
        _openAl.EnsureInitialized();
        _al = _openAl.Al;
        _capture.SamplesAvailable += OnCapturedSamples;
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_isRunning) return;
            try
            {
                InitCodecs();
                _capture.StartAsync(SampleRate).GetAwaiter().GetResult();
                StartPlayback();
                _isRunning = true;
                Debug.WriteLine("[CallAudio] Started");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CallAudio] Start failed: {ex.Message}");
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
            Debug.WriteLine("[CallAudio] Stopped");
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
        Debug.WriteLine($"[CallAudio] Participant added: {userId}");
    }

    public void RemoveParticipant(int userId)
    {
        _playbackQueues.TryRemove(userId, out _);
        Debug.WriteLine($"[CallAudio] Participant removed: {userId}");
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
            Debug.WriteLine($"[CallAudio] Decode error from {fromUserId}: {ex.Message}");
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
            Debug.WriteLine($"[CallAudio] Decode mixed error: {ex.Message}");
        }
    }

    private void OnCapturedSamples(object? sender, short[] samples)
    {
        if (_isMuted || _encoder == null || !_isRunning) return;

        lock (_captureLock)
        {
            int toCopy = Math.Min(samples.Length, _captureBuffer.Length - _captureBufferCount);
            Array.Copy(samples, 0, _captureBuffer, _captureBufferCount, toCopy);
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
            Debug.WriteLine($"[CallAudio] Encode error: {ex.Message}");
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
        _playbackSource = _al.GenSource();
        _al.SetSourceProperty(_playbackSource, SourceBoolean.Looping, false);

        for (int i = 0; i < _playbackRingBuffers.Length; i++)
            _playbackRingBuffers[i] = _al.GenBuffer();

        _playbackReady = true;
        _playbackCts = new CancellationTokenSource();
        _playbackTask = Task.Run(() => PlaybackLoop(_playbackCts.Token));
    }

    private void PlaybackLoop(CancellationToken ct)
    {
        int bufIdx = 0;

        while (!ct.IsCancellationRequested && _playbackReady)
        {
            _al.GetSourceProperty(_playbackSource,
                GetSourceInteger.BuffersProcessed, out int processed);

            for (int i = 0; i < processed; i++)
            {
                uint freed = 0;
                unsafe { _al.SourceUnqueueBuffers(_playbackSource, 1, &freed); }
            }

            if (!MixNextFrame())
            {
                Thread.Sleep(FrameDurationMs / 2);
                continue;
            }

            uint buf = _playbackRingBuffers[bufIdx % _playbackRingBuffers.Length];
            bufIdx++;

            var shortData = ConvertToShort(_mixBuffer);
            unsafe
            {
                fixed (short* ptr = shortData)
                    _al.BufferData(buf, BufferFormat.Mono16,
                        ptr, FrameSamples * sizeof(short), SampleRate);
            }

            unsafe { _al.SourceQueueBuffers(_playbackSource, 1, &buf); }

            _al.GetSourceProperty(_playbackSource,
                GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing)
                _al.SourcePlay(_playbackSource);

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

        _capture.StopAsync().GetAwaiter().GetResult();

        _playbackReady = false;
        _playbackCts?.Cancel();
        try { _playbackTask?.Wait(500); } catch { }
        _playbackCts?.Dispose();
        _playbackCts = null;

        if (_playbackSource != 0)
        {
            try
            {
                _al.SourceStop(_playbackSource);
                _al.GetSourceProperty(_playbackSource,
                    GetSourceInteger.BuffersProcessed, out int p);
                if (p > 0)
                {
                    var tmp = new uint[p];
                    unsafe
                    {
                        fixed (uint* ptr = tmp)
                            _al.SourceUnqueueBuffers(_playbackSource, p, ptr);
                    }
                }
                _al.DeleteSource(_playbackSource);
            }
            catch { }
            _playbackSource = 0;
        }

        foreach (var buf in _playbackRingBuffers)
            if (buf != 0) try { _al.DeleteBuffer(buf); } catch { }

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
        _capture.SamplesAvailable -= OnCapturedSamples;
        lock (_stateLock) { StopInternal(); }
    }
}