namespace Core.Services.Media.Abstractions;

/// <summary>
/// Платформо-независимый захват аудио с микрофона.
/// Реализации: OpenAlCaptureDevice (Desktop), AndroidCaptureDevice (Android).
/// </summary>
public interface IAudioCaptureDevice : IDisposable
{
    /// <summary>Микрофон доступен на текущей платформе/устройстве.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Начать захват. Вызывает <see cref="SamplesAvailable"/> по мере накопления данных.
    /// </summary>
    /// <param name="sampleRate">Частота дискретизации (16000 или 48000).</param>
    /// <param name="ct">Токен отмены.</param>
    Task StartAsync(int sampleRate, CancellationToken ct = default);

    /// <summary>Остановить захват.</summary>
    Task StopAsync();

    /// <summary>
    /// Вызывается из фонового потока когда накоплен чанк PCM Int16 сэмплов.
    /// Размер чанка зависит от реализации.
    /// </summary>
    event EventHandler<short[]>? SamplesAvailable;
}