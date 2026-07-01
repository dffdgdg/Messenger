using Shared.Contracts.Message;

namespace Core.Services.Media.Abstractions;

public readonly record struct DownloadProgressInfo(double? Percentage, long BytesReceived, long? TotalBytes);

public enum DownloadOutcomeStatus { Completed, Cancelled, Failed }

public sealed record DownloadOutcome(DownloadOutcomeStatus Status, string? FilePath, string? Error);

/// <summary>
/// Единая точка запуска скачиваний вложений.
/// Дедуплицирует параллельные запросы на один и тот же файл, ограничивает
/// общее число одновременных закачек и позволяет нескольким подписчикам
///  следить за прогрессом одного файла.
/// </summary>
public interface IDownloadManager
{
    Task<DownloadOutcome> DownloadAsync(MessageFileDto file, IProgress<DownloadProgressInfo>? progress = null, CancellationToken ct = default);

    /// <summary>Отменяет физическую закачку файла — прервётся для всех подписчиков.</summary>
    void CancelDownload(int fileId);

    bool IsDownloading(int fileId);
}