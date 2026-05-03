using PortAudioSharp;
using System;
using System.Diagnostics;
using System.Threading;
using IOStream = System.IO.Stream;

namespace Desktop.Services.Features.Media.Audio;

public sealed class AudioPlayerService : IAudioPlayerService
{
    private readonly PortAudioLifetime _portAudio;
    private Stream? _paStream;
    private WavData? _wavData;
    private Timer? _positionTimer;
    private readonly Lock _lock = new();
    private bool _disposed;

    private long _positionSamples;
    private bool _isPlaying;
    private bool _isPaused;

    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;
    public int? CurrentMessageId { get; private set; }

    public TimeSpan Position
    {
        get
        {
            lock (_lock)
            {
                if (_wavData == null) return TimeSpan.Zero;
                return TimeSpan.FromSeconds(
                    (double)_positionSamples / _wavData.SampleRate);
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_lock)
            {
                return _wavData?.Duration ?? TimeSpan.Zero;
            }
        }
    }

    public event Action<int>? PlaybackStarted;
    public event Action<int>? PlaybackPaused;
    public event Action<int>? PlaybackResumed;
    public event Action<int>? PlaybackStopped;
    public event Action<int, TimeSpan>? PositionChanged;

    public AudioPlayerService(PortAudioLifetime portAudio)
    {
        _portAudio = portAudio;
        _portAudio.EnsureInitialized();
    }

    public void Play(int messageId, IOStream audioStream)
    {
        lock (_lock)
        {
            if (_disposed) return;

            StopInternal(notifyStop: false);

            try
            {
                if (audioStream.CanSeek)
                    audioStream.Position = 0;

                _wavData = WavData.Load(audioStream);
                _positionSamples = 0;
                CurrentMessageId = messageId;

                var outputParams = new StreamParameters
                {
                    device = PortAudio.DefaultOutputDevice,
                    channelCount = _wavData.Channels,
                    sampleFormat = SampleFormat.Int16,
                    suggestedLatency = PortAudio
                        .GetDeviceInfo(PortAudio.DefaultOutputDevice)
                        .defaultLowOutputLatency,
                    hostApiSpecificStreamInfo = IntPtr.Zero
                };

                _paStream = new Stream(inParams: null, outParams: outputParams, sampleRate: _wavData.SampleRate, framesPerBuffer: 512, streamFlags: StreamFlags.ClipOff,
                    callback: (_, output, frameCount, ref _, _, _) => AudioCallback(output, (long)frameCount),
                    userData: IntPtr.Zero);

                _paStream.Start();
                _isPlaying = true;
                _isPaused = false;

                _positionTimer = new Timer(OnPositionTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

                PlaybackStarted?.Invoke(messageId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Play failed: {ex.Message}");
                StopInternal(notifyStop: false);
            }
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (!_isPlaying || _isPaused || _paStream == null) return;

            try
            {
                _paStream.Stop();
                _isPaused = true;
                _isPlaying = false;
                _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                if (CurrentMessageId.HasValue)
                    PlaybackPaused?.Invoke(CurrentMessageId.Value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Pause failed: {ex.Message}");
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!_isPaused || _paStream == null) return;

            try
            {
                _paStream.Start();
                _isPlaying = true;
                _isPaused = false;
                _positionTimer?.Change(
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(50));

                if (CurrentMessageId.HasValue)
                    PlaybackResumed?.Invoke(CurrentMessageId.Value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Resume failed: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_lock) { StopInternal(notifyStop: true); }
    }

    public void Seek(double positionPercent)
    {
        lock (_lock)
        {
            if (_wavData == null) return;

            var clamped = Math.Clamp(positionPercent, 0.0, 1.0);
            _positionSamples = (long)(_wavData.TotalSamples * clamped);

            if (CurrentMessageId.HasValue)
                PositionChanged?.Invoke(CurrentMessageId.Value, Position);
        }
    }

    private StreamCallbackResult AudioCallback(IntPtr output, long frameCount)
    {
        var data = _wavData;
        if (data == null || output == IntPtr.Zero)
            return StreamCallbackResult.Complete;

        var totalSamplesNeeded = (int)(frameCount * data.Channels);
        var pos = Interlocked.Read(ref _positionSamples);
        var remaining = data.Samples.Length - (int)pos;
        var available = Math.Min(totalSamplesNeeded, remaining);

        if (available > 0)
        {
            System.Runtime.InteropServices.Marshal.Copy(data.Samples, (int)pos, output, available);

            if (available < totalSamplesNeeded)
            {
                var silenceSamples = totalSamplesNeeded - available;
                var silencePtr = IntPtr.Add(output, available * sizeof(short));
                var silence = System.Buffers.ArrayPool<short>.Shared.Rent(silenceSamples);
                try
                {
                    Array.Clear(silence, 0, silenceSamples);
                    System.Runtime.InteropServices.Marshal.Copy(silence, 0, silencePtr, silenceSamples);
                }
                finally
                {
                    System.Buffers.ArrayPool<short>.Shared.Return(silence);
                }
            }
        }
        else
        {
            var silence = System.Buffers.ArrayPool<short>.Shared.Rent(totalSamplesNeeded);
            try
            {
                Array.Clear(silence, 0, totalSamplesNeeded);
                System.Runtime.InteropServices.Marshal.Copy(silence, 0, output, totalSamplesNeeded);
            }
            finally
            {
                System.Buffers.ArrayPool<short>.Shared.Return(silence);
            }
        }

        Interlocked.Exchange(ref _positionSamples, pos + available);

        return available < totalSamplesNeeded
            ? StreamCallbackResult.Complete
            : StreamCallbackResult.Continue;
    }

    private void OnPositionTick(object? state)
    {
        int? msgId;
        TimeSpan pos;

        lock (_lock)
        {
            if (_disposed || _wavData == null || CurrentMessageId == null)
                return;

            msgId = CurrentMessageId;
            pos = Position;

            if (_positionSamples >= _wavData.TotalSamples && _isPlaying)
            {
                StopInternal(notifyStop: true);
                return;
            }
        }

        if (msgId.HasValue)
            PositionChanged?.Invoke(msgId.Value, pos);
    }

    private void StopInternal(bool notifyStop)
    {
        var previousId = CurrentMessageId;

        _positionTimer?.Dispose();
        _positionTimer = null;

        if (_paStream != null)
        {
            try
            {
                if (_isPlaying) _paStream.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Stop warning: {ex.Message}");
            }

            try { _paStream.Dispose(); }
            catch { /* игнорируем */ }

            _paStream = null;
        }

        _wavData = null;
        _positionSamples = 0;
        CurrentMessageId = null;
        _isPlaying = false;
        _isPaused = false;

        if (notifyStop && previousId.HasValue)
            PlaybackStopped?.Invoke(previousId.Value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock) { StopInternal(notifyStop: false); }
    }
}