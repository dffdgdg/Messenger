using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Audio;

public interface IAudioRecorderService
{
    /// <summary>Поддерживается ли запись на текущей платформе.</summary>
    bool IsSupported { get; }

    /// <summary>Идёт ли запись прямо сейчас.</summary>
    bool IsRecording { get; }

    /// <summary>Текущая длительность записи.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>Начать запись. Возвращает false если не удалось.</summary>
    Task<bool> StartAsync(CancellationToken ct = default);

    /// <summary>Остановить запись и вернуть поток с аудио (WAV/OGG). null — если ошибка.</summary>
    Task<AudioRecordingResult?> StopAsync(CancellationToken ct = default);

    /// <summary>Отменить запись, ресурсы освобождаются, файл не сохраняется.</summary>
    Task CancelAsync();
}

public sealed class AudioRecordingResult : IDisposable
{
    public required Stream AudioStream { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required TimeSpan Duration { get; init; }

    public void Dispose() => AudioStream.Dispose();
}