#if !ANDROID 
using Core.Services.Features.Media.Abstractions;
using Silk.NET.OpenAL;
using System.Diagnostics;

namespace Core.Services.Features.Media.Audio.Platform.Desktop;

/// <summary>OpenAL Soft playback через Source + streaming buffers.</summary>
public sealed class OpenAlPlaybackDevice : IAudioPlaybackDevice
{
    private const int RingBufferCount = 8; // больше буферов = меньше underrun

    private readonly OpenAlLifetime _lifetime;
    private readonly AL _al;
    private readonly Queue<uint> _freeBuffers = new();
    private uint _source;
    private readonly uint[] _ringBuffers = new uint[RingBufferCount];
    private bool _sourceCreated;

    private int _sampleRate;
    private int _channels;
    private BufferFormat _format;

    private CancellationTokenSource? _cts;
    private Task? _feedTask;
    private readonly Queue<short[]> _queue = new();
    private readonly Lock _queueLock = new();

    private bool _disposed;

    public bool IsAvailable => _lifetime.IsAvailable;

    public OpenAlPlaybackDevice(OpenAlLifetime lifetime)
    {
        _lifetime = lifetime;
        _lifetime.EnsureInitialized();
        _al = _lifetime.Al;
    }

    // OpenAlPlaybackDevice.cs — полная замена FeedLoop и InitAsync

    public Task InitAsync(int sampleRate, int channels, CancellationToken ct = default)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _format = channels == 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;

        _source = _al.GenSource();
        _al.SetSourceProperty(_source, SourceBoolean.Looping, false);
        _al.SetSourceProperty(_source, SourceFloat.Gain, 1.0f);

        for (int i = 0; i < RingBufferCount; i++)
            _ringBuffers[i] = _al.GenBuffer();

        _sourceCreated = true;

        // Кладём все буферы в пул свободных — не в очередь OpenAL
        lock (_queueLock)
        {
            foreach (var buf in _ringBuffers)
                _freeBuffers.Enqueue(buf);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _feedTask = Task.Run(() => FeedLoop(_cts.Token), ct);

        Debug.WriteLine($"[OpenAlPlayback] Init sampleRate={sampleRate} ch={channels}");
        return Task.CompletedTask;
    }

    public void EnqueueSamples(short[] samples)
    {
        lock (_queueLock)
        {
            // Не накапливаем больше ~500ms
            if (_queue.Count < 25)
                _queue.Enqueue(samples);
        }
    }

    public void Flush()
    {
        // Очищаем программную очередь
        lock (_queueLock) { _queue.Clear(); }

        // Останавливаем source и забираем буферы из OpenAL обратно в пул
        if (!_sourceCreated) return;

        _al.SourceStop(_source);

        _al.GetSourceProperty(_source,
            GetSourceInteger.BuffersProcessed, out int processed);
        _al.GetSourceProperty(_source,
            GetSourceInteger.BuffersQueued, out int queued);

        int total = processed + queued;
        if (total <= 0) return;

        var tmp = new uint[total];
        unsafe
        {
            fixed (uint* ptr = tmp)
                _al.SourceUnqueueBuffers(_source, total, ptr);
        }

        lock (_queueLock)
        {
            foreach (var b in tmp)
                _freeBuffers.Enqueue(b);
        }
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

        if (_sourceCreated)
        {
            try
            {
                _al.SourceStop(_source);
                UnqueueAll();
                _al.DeleteSource(_source);
            }
            catch { }

            for (int i = 0; i < RingBufferCount; i++)
            {
                if (_ringBuffers[i] != 0)
                {
                    try { _al.DeleteBuffer(_ringBuffers[i]); } catch { }
                    _ringBuffers[i] = 0;
                }
            }

            _sourceCreated = false;
            _source = 0;
        }

        lock (_queueLock)
        {
            _queue.Clear();
            _freeBuffers.Clear();
        }
    }

    private void FeedLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _sourceCreated)
        {
            // ── Шаг 1: забираем буферы которые OpenAL уже воспроизвёл ──────
            _al.GetSourceProperty(_source,
                GetSourceInteger.BuffersProcessed, out int processed);

            for (int i = 0; i < processed; i++)
            {
                uint returned = 0;
                unsafe { _al.SourceUnqueueBuffers(_source, 1, &returned); }

                lock (_queueLock) { _freeBuffers.Enqueue(returned); }
            }

            // ── Шаг 2: заполняем свободные буферы данными из очереди ────────
            while (true)
            {
                short[]? chunk = null;
                uint freeBuf = 0;
                bool hasBoth = false;

                lock (_queueLock)
                {
                    if (_queue.Count > 0 && _freeBuffers.Count > 0)
                    {
                        chunk = _queue.Dequeue();
                        freeBuf = _freeBuffers.Dequeue();
                        hasBoth = true;
                    }
                }

                if (!hasBoth) break;

                // Загружаем данные в буфер
                unsafe
                {
                    fixed (short* ptr = chunk)
                        _al.BufferData(freeBuf, _format,
                            ptr, chunk.Length * sizeof(short), _sampleRate);
                }

                // Ставим буфер в очередь OpenAL
                unsafe { _al.SourceQueueBuffers(_source, 1, &freeBuf); }
            }

            // ── Шаг 3: запускаем/перезапускаем source если нужно ────────────
            _al.GetSourceProperty(_source,
                GetSourceInteger.BuffersQueued, out int queued);

            if (queued > 0)
            {
                _al.GetSourceProperty(_source,
                    GetSourceInteger.SourceState, out int state);

                if ((SourceState)state != SourceState.Playing)
                {
                    _al.SourcePlay(_source);
                    Debug.WriteLine("[OpenAlPlayback] Source (re)started");
                }
            }

            Thread.Sleep(5);
        }
    }
    private void UnqueueAll()
    {
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int p);
        if (p > 0)
        {
            var tmp = new uint[p];
            unsafe { fixed (uint* ptr = tmp) _al.SourceUnqueueBuffers(_source, p, ptr); }
        }

        _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int q);
        if (q > 0)
        {
            var tmp2 = new uint[q];
            unsafe { fixed (uint* ptr = tmp2) _al.SourceUnqueueBuffers(_source, q, ptr); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseAsync().GetAwaiter().GetResult();
    }
}
#endif