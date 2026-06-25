using Core.Data.Models.Cache;
using Core.Data.Repositories.Abstractions;
using Shared.Contracts.Message;
using System.Diagnostics;

namespace Core.Services.Features.Media.Files;

public sealed class FileDownloadStateService(IDownloadedFileRepository repository) : IFileDownloadStateService
{
    public async Task<FileDownloadState> GetStateAsync(MessageFileDto file)
    {
        CachedDownloadedFile? cached;
        try
        {
            cached = await repository.FindByFileIdAsync(file.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileDownloadState] GetStateAsync error: {ex.Message}");
            return new FileDownloadState(FileDownloadStatus.NotDownloaded, null);
        }

        if (cached is null)
            return new FileDownloadState(FileDownloadStatus.NotDownloaded, null);

        if (!File.Exists(cached.LocalPath))
        {
            try { await repository.DeleteByFileIdAsync(file.Id); }
            catch { /* best-effort */ }

            return new FileDownloadState(FileDownloadStatus.Missing, null);
        }

        if (file.FileSize > 0 && cached.FileSize != file.FileSize)
            return new FileDownloadState(FileDownloadStatus.Changed, cached.LocalPath);

        return new FileDownloadState(FileDownloadStatus.Downloaded, cached.LocalPath);
    }

    public async Task RegisterDownloadAsync(MessageFileDto file, string localPath)
    {
        var entry = new CachedDownloadedFile
        {
            FileId = file.Id,
            MessageId = file.MessageId,
            LocalPath = localPath,
            FileName = file.FileName,
            FileSize = file.FileSize,
            DownloadedAtTicks = DateTime.UtcNow.Ticks,
            ContentType = file.ContentType
        };

        try
        {
            await repository.UpsertAsync(entry);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileDownloadState] RegisterDownloadAsync error: {ex.Message}");
        }
    }

    public async Task ResetAsync(int fileId)
    {
        try
        {
            await repository.DeleteByFileIdAsync(fileId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileDownloadState] ResetAsync error: {ex.Message}");
        }
    }

    public async Task CleanupOrphanedAsync()
    {
        try
        {
            await repository.DeleteOrphanedAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileDownloadState] CleanupOrphanedAsync error: {ex.Message}");
        }
    }
}