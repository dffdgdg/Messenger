using Core.Services.Features.Media.Abstractions;

namespace Core.Services.Features.Media.Audio.Platform.Android;

/// <summary>
/// Заглушка для Android. Реализация через AudioRecord JNI добавляется позже.
/// </summary>
public sealed class AndroidCaptureDevice : IAudioCaptureDevice
{
    public bool IsAvailable => false;

    public event EventHandler<short[]>? SamplesAvailable;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        // TODO: AudioRecord через Microsoft.Maui.Essentials или JNI биндинги
        throw new PlatformNotSupportedException("Android audio capture не реализован. Используйте OpenAlCaptureDevice на Core.");
    }

    public Task StopAsync() => Task.CompletedTask;

    public void Dispose() { }
}