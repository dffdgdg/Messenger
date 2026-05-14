using PortAudioSharp;
using System.Diagnostics;

namespace Desktop.Services.Features.Media.Audio;

/// <summary>
/// Singleton — владеет единственным вызовом PortAudio.Initialize() / Terminate().
/// Все аудио-сервисы получают его через DI вместо самостоятельной инициализации.
/// </summary>
public sealed class PortAudioLifetime : IDisposable
{
    private bool _initialized;
    private bool _disposed;
    private readonly Lock _lock = new();

    public void EnsureInitialized()
    {
        lock (_lock)
        {
            if (_initialized) return;
            PortAudio.Initialize();
            _initialized = true;
            Debug.WriteLine("[PortAudio] Initialized");
        }
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                EnsureInitialized();
                return PortAudio.DefaultInputDevice != PortAudio.NoDevice || PortAudio.DefaultOutputDevice != PortAudio.NoDevice;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PortAudio] IsAvailable check failed: {ex.Message}");
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed || !_initialized) return;
            _disposed = true;

            try
            {
                PortAudio.Terminate();
                Debug.WriteLine("[PortAudio] Terminated");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PortAudio] Terminate warning: {ex.Message}");
            }
        }
    }
}