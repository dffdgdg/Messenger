using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace MessengerDesktop.Services.Audio;

public interface IAudioPlayerService : IDisposable
{
    bool IsPlaying { get; }
    bool IsPaused { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int? CurrentMessageId { get; }
    event Action<int>? PlaybackStarted;
    event Action<int>? PlaybackPaused;
    event Action<int>? PlaybackResumed;
    event Action<int>? PlaybackStopped;
    event Action<int, TimeSpan>? PositionChanged;

    void Play(int messageId, Stream audioStream);
    void Pause();
    void Resume();
    void Stop();
    void Seek(double positionPercent);
}

public sealed class AudioPlayerService : IAudioPlayerService
{
    private WaveOutEvent? _waveOut;
    private WaveStream? _waveStream;
    private Timer? _positionTimer;
    private readonly object _lock = new();
    private bool _disposed;

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _waveOut?.PlaybackState == PlaybackState.Paused;
    public int? CurrentMessageId { get; private set; }

    public TimeSpan Position
    {
        get
        {
            lock (_lock)
            {
                return _waveStream?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_lock)
            {
                return _waveStream?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    public event Action<int>? PlaybackStarted;
    public event Action<int>? PlaybackPaused;
    public event Action<int>? PlaybackResumed;
    public event Action<int>? PlaybackStopped;
    public event Action<int, TimeSpan>? PositionChanged;

    public void Play(int messageId, Stream audioStream)
    {
        lock (_lock)
        {
            if (_disposed) return;

            StopInternal();

            try
            {
                if (audioStream.CanSeek)
                    audioStream.Position = 0;

                _waveStream = new WaveFileReader(audioStream);

                _waveOut = new WaveOutEvent
                {
                    DesiredLatency = 200
                };

                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Init(_waveStream);

                CurrentMessageId = messageId;

                _waveOut.Play();

                _positionTimer = new Timer(OnPositionTimerTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

                PlaybackStarted?.Invoke(messageId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Play failed: {ex.Message}");
                StopInternal();
            }
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_waveOut?.PlaybackState != PlaybackState.Playing) return;

            _waveOut.Pause();
            _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            if (CurrentMessageId.HasValue)
                PlaybackPaused?.Invoke(CurrentMessageId.Value);
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_waveOut?.PlaybackState != PlaybackState.Paused) return;

            _waveOut.Play();
            _positionTimer?.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

            if (CurrentMessageId.HasValue)
                PlaybackResumed?.Invoke(CurrentMessageId.Value);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    public void Seek(double positionPercent)
    {
        lock (_lock)
        {
            if (_waveStream == null) return;

            var clampedPercent = Math.Clamp(positionPercent, 0.0, 1.0);
            var targetTime = TimeSpan.FromTicks(
                (long)(_waveStream.TotalTime.Ticks * clampedPercent));

            _waveStream.CurrentTime = targetTime;

            if (CurrentMessageId.HasValue)
                PositionChanged?.Invoke(CurrentMessageId.Value, targetTime);
        }
    }

    private void OnPositionTimerTick(object? state)
    {
        lock (_lock)
        {
            if (_disposed || _waveStream == null || CurrentMessageId == null) return;

            try
            {
                PositionChanged?.Invoke(CurrentMessageId.Value, _waveStream.CurrentTime);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Position tick error: {ex.Message}");
            }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            Debug.WriteLine($"[AudioPlayer] Playback error: {e.Exception.Message}");

        int? msgId;
        lock (_lock)
        {
            msgId = CurrentMessageId;
        }

        if (msgId.HasValue)
            PlaybackStopped?.Invoke(msgId.Value);

        lock (_lock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        var previousId = CurrentMessageId;

        _positionTimer?.Dispose();
        _positionTimer = null;

        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;

            try
            {
                if (_waveOut.PlaybackState != PlaybackState.Stopped)
                    _waveOut.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Stop error: {ex.Message}");
            }

            _waveOut.Dispose();
            _waveOut = null;
        }

        _waveStream?.Dispose();
        _waveStream = null;

        CurrentMessageId = null;

        if (previousId.HasValue)
            PlaybackStopped?.Invoke(previousId.Value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            StopInternal();
        }
    }
}