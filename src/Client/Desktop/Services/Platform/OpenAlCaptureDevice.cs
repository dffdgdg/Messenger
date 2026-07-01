using Core.Services.Media.Abstractions;
using System.Diagnostics;

namespace Desktop.Shared.Services.Platform;

public sealed class OpenAlCaptureDevice : IAudioCaptureDevice, IAsyncDisposable
{
    private const int AlcFormatMono16 = 0x1101;
    private const int AlcCaptureSamples = 0x312;

    private nint _device;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private int _chunkSamples;
    private bool _disposed;

    public bool IsAvailable => CheckAvailable();

    public event EventHandler<short[]>? SamplesAvailable;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (_device != 0) return Task.CompletedTask;

        _device = OpenAlNative.CaptureOpenDevice(null, (uint)sampleRate, AlcFormatMono16, sampleRate / 2);

        if (_device == 0)
        {
            Debug.WriteLine("[OpenAlCapture] Не удалось открыть capture device");
            return Task.CompletedTask;
        }

        _chunkSamples = sampleRate / 50;

        OpenAlNative.CaptureStart(_device);

        var oldCts = _cts;
        oldCts?.Cancel();
        oldCts?.Dispose();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _captureTask = Task.Run(() => CaptureLoop(_cts.Token), _cts.Token);

        Debug.WriteLine($"[OpenAlCapture] Started, sampleRate={sampleRate}");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);

        if (cts is not null)    
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (_captureTask != null)
        {
            try { await _captureTask.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch { /* таймаут или отмена — ожидаемо */ }
            _captureTask = null;
        }

        CloseDevice();
        Debug.WriteLine("[OpenAlCapture] Stopped");
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var buffer = new short[_chunkSamples];

        while (!ct.IsCancellationRequested && _device != 0)
        {
            OpenAlNative.GetIntegerv(_device, AlcCaptureSamples, 1, out int available);

            if (available >= _chunkSamples)
            {
                unsafe
                {
                    fixed (short* ptr = buffer)
                        OpenAlNative.CaptureSamples(_device, (nint)ptr, _chunkSamples);
                }

                var copy = new short[_chunkSamples];
                buffer.AsSpan().CopyTo(copy);
                SamplesAvailable?.Invoke(this, copy);
            }
            else
            {
                Thread.Sleep(5);
            }
        }
    }

    private static bool CheckAvailable()
    {
        try
        {
            var test = OpenAlNative.CaptureOpenDevice(null, 16000, AlcFormatMono16, 8000);
            if (test == 0) return false;
            OpenAlNative.CaptureCloseDevice(test);
            return true;
        }
        catch { return false; }
    }

    private void CloseDevice()
    {
        if (_device == 0) return;
        try { OpenAlNative.CaptureStop(_device); } catch { }
        try { OpenAlNative.CaptureCloseDevice(_device); } catch { }
        _device = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        CloseDevice();
    }
}