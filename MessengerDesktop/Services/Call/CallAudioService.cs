using Concentus;
using Concentus.Enums;
using MessengerDesktop.Services.Audio;
using PortAudioSharp;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MessengerDesktop.Services.Call;

public sealed class CallAudioService : IDisposable
{
    private const int SampleRate = 48000;
    private const int Channels = 1;
    private const int FrameDurationMs = 20;
    private const int FrameSamples = SampleRate * FrameDurationMs / 1000; // 960
    private const int MaxEncodedBytes = 4000;

    private Stream? _inputStream;
    private Stream? _outputStream;
    private IOpusEncoder? _encoder;
    private IOpusDecoder? _decoder;
    private readonly short[] _captureBuffer = new short[FrameSamples * 4];
    private int _captureBufferCount;
    private readonly Lock _captureLock = new();
    private readonly ConcurrentDictionary<int, ConcurrentQueue<float[]>> _playbackQueues = new();
    private readonly float[] _mixBuffer = new float[FrameSamples];
    private bool _isRunning;
    private bool _isMuted;
    private bool _disposed;
    private readonly Lock _stateLock = new();

    private readonly PortAudioLifetime _portAudio;

    public event Action<byte[], int>? OnEncodedFrame;

    public bool IsRunning => _isRunning;
    public bool IsMuted => _isMuted;

    public CallAudioService(PortAudioLifetime portAudio)
    {
        _portAudio = portAudio;
        _portAudio.EnsureInitialized();
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_isRunning) return;

            try
            {
                InitCodecs();
                StartInputStream();
                StartOutputStream();
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
            var samplesDecoded = _decoder.Decode(opusData.AsSpan(0, length), decoded.AsSpan(), FrameSamples);

            if (samplesDecoded > 0 && queue.Count < 10)
                queue.Enqueue(decoded);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CallAudio] Decode error from {fromUserId}: {ex.Message}");
        }
    }

    private void StartInputStream()
    {
        if (PortAudio.DefaultInputDevice == PortAudio.NoDevice)
            throw new InvalidOperationException("Нет устройства ввода");

        var inputParams = new StreamParameters
        {
            device = PortAudio.DefaultInputDevice,
            channelCount = Channels,
            sampleFormat = SampleFormat.Int16,
            suggestedLatency = PortAudio.GetDeviceInfo(PortAudio.DefaultInputDevice).defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        _inputStream = new PortAudioSharp.Stream(inputParams, null, SampleRate, FrameSamples / 2, StreamFlags.ClipOff, InputCallback, IntPtr.Zero);

        _inputStream.Start();
    }

    private StreamCallbackResult InputCallback(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (input == IntPtr.Zero || _isMuted || _encoder == null)
            return StreamCallbackResult.Continue;

        var sampleCount = (int)frameCount * Channels;

        var incoming = new short[sampleCount];
        Marshal.Copy(input, incoming, 0, sampleCount);

        lock (_captureLock)
        {
            var toCopy = Math.Min(sampleCount, _captureBuffer.Length - _captureBufferCount);
            Array.Copy(incoming, 0, _captureBuffer, _captureBufferCount, toCopy);
            _captureBufferCount += toCopy;

            while (_captureBufferCount >= FrameSamples)
            {
                EncodeAndSend(_captureBuffer.AsSpan(0, FrameSamples));

                _captureBufferCount -= FrameSamples;
                if (_captureBufferCount > 0)
                {
                    Array.Copy(_captureBuffer, FrameSamples, _captureBuffer, 0, _captureBufferCount);
                }
            }
        }

        return StreamCallbackResult.Continue;
    }

    private void EncodeAndSend(ReadOnlySpan<short> pcmFrame)
    {
        if (_encoder == null) return;

        try
        {
            Span<byte> encoded = stackalloc byte[MaxEncodedBytes];
            var encodedLength = _encoder.Encode(pcmFrame, FrameSamples, encoded, MaxEncodedBytes);

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

    private void StartOutputStream()
    {
        if (PortAudio.DefaultOutputDevice == PortAudio.NoDevice)
            throw new InvalidOperationException("Нет устройства вывода");

        var outputParams = new StreamParameters
        {
            device = PortAudio.DefaultOutputDevice,
            channelCount = Channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = PortAudio.GetDeviceInfo(PortAudio.DefaultOutputDevice).defaultLowOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        _outputStream = new PortAudioSharp.Stream(null, outputParams, SampleRate, FrameSamples, StreamFlags.ClipOff, OutputCallback, IntPtr.Zero);

        _outputStream.Start();
    }

    private StreamCallbackResult OutputCallback(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (output == IntPtr.Zero)
            return StreamCallbackResult.Continue;

        var samplesNeeded = (int)frameCount * Channels;

        Array.Clear(_mixBuffer, 0, samplesNeeded);
        var hasAudio = false;

        foreach (var queue in _playbackQueues.Values)
        {
            if (!queue.TryDequeue(out var frame)) continue;

            hasAudio = true;
            var count = Math.Min(frame.Length, samplesNeeded);

            for (var i = 0; i < count; i++)
            {
                _mixBuffer[i] += frame[i];
            }
        }

        if (hasAudio)
        {
            for (var i = 0; i < samplesNeeded; i++)
                _mixBuffer[i] = Math.Clamp(_mixBuffer[i], -1.0f, 1.0f);

            Marshal.Copy(_mixBuffer, 0, output, samplesNeeded);
        }
        else
        {
            var silence = new float[samplesNeeded];
            Marshal.Copy(silence, 0, output, samplesNeeded);
        }

        return StreamCallbackResult.Continue;
    }

    private void InitCodecs()
    {
        _encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);

        _encoder.Bitrate = 32000;
        _encoder.Complexity = 5;
        _encoder.UseVBR = true;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

        _decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);

        Debug.WriteLine("[CallAudio] Codecs initialized");
    }

    private void StopInternal()
    {
        _isRunning = false;

        StopStream(ref _inputStream);
        StopStream(ref _outputStream);

        _playbackQueues.Clear();
        _captureBufferCount = 0;
    }

    private static void StopStream(ref PortAudioSharp.Stream? stream)
    {
        if (stream == null) return;
        try { stream.Stop(); } catch { /* игнорируем */ }
        try { stream.Dispose(); } catch { /* игнорируем */ }
        stream = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_stateLock) { StopInternal(); }
    }
}