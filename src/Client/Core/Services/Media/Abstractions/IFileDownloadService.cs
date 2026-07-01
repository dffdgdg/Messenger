namespace Core.Services.Media.Abstractions;

/// <summary>Прогресс низкоуровневой закачки в конкретный файл на диске.</summary>
public readonly record struct FileFetchProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percentage => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value * 100 : null;
}

public enum FileFetchStatus { Completed, Cancelled }

public sealed record FileFetchResult(FileFetchStatus Status, string FilePath, long BytesWritten);

public interface IFileDownloadService
{
    /// <summary>
    /// Скачивает <paramref name="url"/> в <paramref name="destinationPath"/>.
    /// Поддерживает докачку: если рядом уже лежит "{destinationPath}.part" —
    /// шлёт Range-заголовок и дописывает. При отмене .part остаётся на диске
    /// для последующей докачки.
    /// </summary>
    Task<FileFetchResult> FetchAsync(string url, string destinationPath, IProgress<FileFetchProgress>? progress = null, CancellationToken ct = default);

    string GetDownloadsFolder();

    /// <summary>Копирует уже скачанный (закэшированный) файл в системную папку "Загрузки" пользователя.</summary>
    Task<string> ExportToDownloadsAsync(string sourceFilePath, string suggestedFileName, CancellationToken ct = default);

    Task OpenFileAsync(string filePath);
    Task OpenFolderAsync(string folderPath);
}