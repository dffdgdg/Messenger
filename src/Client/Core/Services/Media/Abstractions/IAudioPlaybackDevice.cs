namespace Core.Services.Media.Abstractions;

/// <summary>
/// Платформо-независимое воспроизведение PCM аудио.
/// Реализации: OpenAlPlaybackDevice (Desktop), AndroidPlaybackDevice (Android).
/// </summary>
public interface IAudioPlaybackDevice : IDisposable
{
    /// <summary>Устройство вывода доступно.</summary>
    bool IsAvailable { get; }

    /// <summary>Инициализировать устройство с нужной частотой и числом каналов.</summary>
    Task InitAsync(int sampleRate, int channels, CancellationToken ct = default);

    /// <summary>Поставить PCM Int16 сэмплы в очередь воспроизведения.</summary>
    void EnqueueSamples(short[] samples);

    /// <summary>Очистить очередь (при смене трека или seek).</summary>
    void Flush();

    /// <summary>Освободить устройство (без Dispose всего объекта).</summary>
    Task ReleaseAsync();
}