using Silk.NET.OpenAL;
using System.Diagnostics;

namespace Desktop.Shared.Services.Platform;

public sealed class OpenAlLifetime : IDisposable
{
    private readonly Lock _lock = new();
    private bool _initialized;
    private bool _disposed;

    private ALContext? _alc;
    private unsafe Device* _device;
    private unsafe Context* _context;

    public AL Al { get; private set; } = null!;
    public ALContext Alc { get; private set; } = null!;

    public void EnsureInitialized()
    {
        lock (_lock)
        {
            if (_initialized) return;
            InitializeInternal();
            _initialized = true;
            Debug.WriteLine("[OpenAL] Initialized");
        }
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                EnsureInitialized();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenAL] IsAvailable check failed: {ex.Message}");
                return false;
            }
        }
    }

    // Имя устройства захвата — null = системный дефолт
    public string? CaptureDeviceName { get; set; } = null;
    public string? PlaybackDeviceName { get; set; } = null;

    private unsafe void InitializeInternal()
    {
        _alc = ALContext.GetApi(soft: true);
        Al = AL.GetApi(soft: true);

        // Открываем playback устройство
        _device = _alc.OpenDevice(PlaybackDeviceName);
        if (_device == null)
            throw new InvalidOperationException("[OpenAL] Не удалось открыть устройство воспроизведения");

        // Создаём контекст
        _context = _alc.CreateContext(_device, null);
        if (_context == null)
            throw new InvalidOperationException("[OpenAL] Не удалось создать контекст");

        if (!_alc.MakeContextCurrent(_context))
            throw new InvalidOperationException("[OpenAL] Не удалось активировать контекст");

        Alc = _alc;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed || !_initialized) return;
            _disposed = true;
            DisposeInternal();
        }
    }

    private unsafe void DisposeInternal()
    {
        try
        {
            if (_alc != null)
            {
                if (_context != null)
                {
                    _alc.MakeContextCurrent(null);
                    _alc.DestroyContext(_context);
                    _context = null;
                }

                if (_device != null)
                {
                    _alc.CloseDevice(_device);
                    _device = null;
                }
            }

            Al?.Dispose();
            _alc?.Dispose();

            Debug.WriteLine("[OpenAL] Disposed");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OpenAL] Dispose warning: {ex.Message}");
        }
    }
}