using Android.Media;
using Core.Services.Features.Media.Abstractions;
using System.Diagnostics;

namespace Mobile.Android.Services.Platform;

public sealed class AndroidPlaybackDevice : IAudioPlaybackDevice
{
    private AudioTrack? _track;
    private CancellationTokenSource? _cts;
    private Task? _feedTask;
    private readonly Queue<short[]> _queue = new();
    private readonly Lock _queueLock = new();

    private int _sampleRate;
    private int _channels;
    private Encoding _encoding = Encoding.Pcm16bit;

    private bool _disposed;
    private bool _initialized;

    public bool IsAvailable => true;

    public Task InitAsync(int sampleRate, int channels, CancellationToken ct = default)
    {
        _sampleRate = sampleRate;
        _channels = channels;

        var attributes = new AudioAttributes.Builder()
            ?.SetUsage(AudioUsageKind.Media)
            ?.SetContentType(AudioContentType.Music)
            ?.Build();

        var format = new AudioFormat.Builder()
            ?.SetSampleRate(sampleRate)
            ?.SetEncoding(_encoding)
            ?.SetChannelMask(channels == 2 ? ChannelOut.Stereo : ChannelOut.Mono)
            ?.Build();

        int bufferSize = AudioTrack.GetMinBufferSize(
            sampleRate,
            channels == 2 ? ChannelOut.Stereo : ChannelOut.Mono,
            _encoding
        );

        bufferSize = Math.Max(bufferSize, sampleRate * channels * 2 / 5);

        _track = new AudioTrack.Builder()
            ?.SetAudioAttributes(attributes)
            ?.SetAudioFormat(format)
            ?.SetBufferSizeInBytes(bufferSize)
            ?.SetTransferMode(AudioTrackMode.Stream)
            ?.Build();

        if (_track?.State != AudioTrackState.Initialized)
        {
            _track?.Dispose();
            _track = null;
            throw new InvalidOperationException("AudioTrack init failed");
        }

        _track.Play();
        _initialized = true;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _feedTask = Task.Run(() => FeedLoop(_cts.Token), ct);

        Debug.WriteLine($"[AndroidPlayback] Init sampleRate={sampleRate} ch={channels} bufferSize={bufferSize}");
        return Task.CompletedTask;
    }

    public void EnqueueSamples(short[] samples)
    {
        lock (_queueLock)
        {
            if (_queue.Count < 25)
                _queue.Enqueue(samples);
        }
    }

    public void Flush()
    {
        lock (_queueLock)
        {
            _queue.Clear();
        }

        _track?.Flush();
        _track?.Pause();
        _track?.Play();
    }

    public async Task ReleaseAsync()
    {
        _cts?.Cancel();

        if (_feedTask != null)
        {
            try { await _feedTask.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch { }
            _feedTask = null;
        }

        if (_track != null)
        {
            try
            {
                _track.Stop();
                _track.Flush();
                _track.Release();
                _track.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AndroidPlayback] Release error: {ex.Message}");
            }
            _track = null;
        }

        _initialized = false;

        lock (_queueLock)
        {
            _queue.Clear();
        }
    }

    private void FeedLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _initialized && _track != null)
        {
            short[]? chunk = null;

            lock (_queueLock)
            {
                if (_queue.Count > 0)
                    chunk = _queue.Dequeue();
            }

            if (chunk != null && chunk.Length > 0)
            {
                try
                {
                    int written = _track.Write(chunk, 0, chunk.Length);
                    if (written < 0)
                    {
                        Debug.WriteLine($"[AndroidPlayback] Write failed: {written}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AndroidPlayback] FeedLoop error: {ex.Message}");
                    break;
                }
            }
            else
            {
                Thread.Sleep(5);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}