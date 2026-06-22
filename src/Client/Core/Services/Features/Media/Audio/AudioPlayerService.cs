using Core.Services.Features.Media.Abstractions;
using System.Diagnostics;
using IOStream = System.IO.Stream;

namespace Core.Services.Features.Media.Audio;

public sealed class AudioPlayerService(IAudioPlaybackDevice playback) : IAudioPlayerService
{
    // Размер чанка подачи в playback device — 20ms при sampleRate файла
    private const int ChunkMs = 20;
    private WavData? _wavData;
    private CancellationTokenSource? _playCts;
    private readonly Lock _lock = new();
    private bool _disposed;

    private long _positionSamples; // в сэмплах (с учётом каналов)
    private bool _isPlaying;
    private bool _isPaused;
    private readonly SemaphoreSlim _pauseGate = new(1, 1);

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
                    (double)(_positionSamples / _wavData.Channels) / _wavData.SampleRate);
            }
        }
    }

    public TimeSpan Duration
    {
        get { lock (_lock) { return _wavData?.Duration ?? TimeSpan.Zero; } }
    }

    public event Action<int>? PlaybackStarted;
    public event Action<int>? PlaybackPaused;
    public event Action<int>? PlaybackResumed;
    public event Action<int>? PlaybackStopped;
    public event Action<int, TimeSpan>? PositionChanged;

    public void Play(int messageId, IOStream audioStream)
    {
        lock (_lock)
        {
            if (_disposed) return;

            StopInternal(notifyStop: false);

            try
            {
                if (audioStream.CanSeek) audioStream.Position = 0;
                _wavData = WavData.Load(audioStream);
                _positionSamples = 0;
                CurrentMessageId = messageId;
                _isPlaying = true;
                _isPaused = false;
                _pauseGate.Wait(0); // сбрасываем если был заблокирован

                _playCts = new CancellationTokenSource();
                var token = _playCts.Token;

                // Инициализируем устройство и запускаем фоновую подачу
                _ = PlayInternalAsync(token);

                PlaybackStarted?.Invoke(messageId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Play failed: {ex.Message}");
                StopInternal(notifyStop: false);
            }
        }
    }

    private async Task PlayInternalAsync(CancellationToken ct)
    {
        var wavData = _wavData!;

        await playback.InitAsync(wavData.SampleRate, wavData.Channels, ct);

        int chunkSamples = wavData.SampleRate * wavData.Channels * ChunkMs / 1000;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Пауза
                if (_isPaused)
                {
                    await _pauseGate.WaitAsync(ct);
                    _pauseGate.Release();
                }

                long pos;
                lock (_lock) { pos = _positionSamples; }

                if (pos >= wavData.Samples.Length) break;

                int remaining = wavData.Samples.Length - (int)pos;
                int count = Math.Min(chunkSamples, remaining);

                var chunk = new short[count];
                wavData.Samples.AsSpan((int)pos, count).CopyTo(chunk);

                playback.EnqueueSamples(chunk);

                lock (_lock) { _positionSamples += count; }

                // Уведомляем UI о позиции каждые ~50ms (каждые ~2.5 чанка)
                if (CurrentMessageId.HasValue)
                    PositionChanged?.Invoke(CurrentMessageId.Value, Position);

                // Небольшая задержка чтобы не забивать очередь
                await Task.Delay(ChunkMs / 2, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioPlayer] PlayInternal error: {ex.Message}");
        }
        finally
        {
            // Ждём пока устройство доиграет последний чанк
            await Task.Delay(100, ct);
            await playback.ReleaseAsync();

            bool shouldNotify;
            int? mid;
            lock (_lock)
            {
                shouldNotify = _isPlaying && !ct.IsCancellationRequested;
                mid = CurrentMessageId;
                if (shouldNotify) StopInternal(notifyStop: false);
            }

            if (shouldNotify && mid.HasValue)
                PlaybackStopped?.Invoke(mid.Value);
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (!_isPlaying || _isPaused) return;
            _isPaused = false; // будет установлен ниже
            _isPlaying = false;
            _isPaused = true;
            // Блокируем pauseGate чтобы фоновый поток встал
            _pauseGate.Wait(0);
            if (CurrentMessageId.HasValue)
                PlaybackPaused?.Invoke(CurrentMessageId.Value);
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!_isPaused) return;
            _isPaused = false;
            _isPlaying = true;
            // Разблокируем фоновый поток
            if (_pauseGate.CurrentCount == 0)
                _pauseGate.Release();
            if (CurrentMessageId.HasValue)
                PlaybackResumed?.Invoke(CurrentMessageId.Value);
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
            _positionSamples = (long)(_wavData.TotalSamples * clamped) * _wavData.Channels;
            playback.Flush();
            if (CurrentMessageId.HasValue)
                PositionChanged?.Invoke(CurrentMessageId.Value, Position);
        }
    }

    private void StopInternal(bool notifyStop)
    {
        var previousId = CurrentMessageId;

        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = null;

        // Разблокируем если на паузе
        if (_pauseGate.CurrentCount == 0)
            _pauseGate.Release();

        playback.Flush();

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
        _pauseGate.Dispose();
    }
}