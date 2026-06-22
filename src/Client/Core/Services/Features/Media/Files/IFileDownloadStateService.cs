namespace Core.Services.Features.Media.Files;

public enum FileDownloadStatus
{
    NotDownloaded,
    Downloaded,
    Changed,
    Missing
}

public sealed record FileDownloadState(FileDownloadStatus Status,string? LocalPath);

public interface IFileDownloadStateService
{
    Task<FileDownloadState> GetStateAsync(MessageFileDto file);
    Task RegisterDownloadAsync(MessageFileDto file, string localPath);
    Task ResetAsync(int fileId);
    Task CleanupOrphanedAsync();
}