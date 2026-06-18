using Core.Services.Features.Media.Abstractions;

namespace Core.Services.Features.Media.Audio.Platform.Android;

/// <summary>
/// Заглушка для Android. Реализация через AudioTrack JNI добавляется позже.
/// </summary>
public sealed class AndroidPlaybackDevice : IAudioPlaybackDevice
{
    public bool IsAvailable => false;

    public Task InitAsync(int sampleRate, int channels, CancellationToken ct = default)
    {
        throw new PlatformNotSupportedException(
            "Android audio playback не реализован. Используйте OpenAlPlaybackDevice на Core.");
    }

    public void EnqueueSamples(short[] samples) { }
    public void Flush() { }
    public Task ReleaseAsync() => Task.CompletedTask;
    public void Dispose() { }
}