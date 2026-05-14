using Desktop.Data.Models.Cache;

namespace Desktop.Data.Repositories.Abstractions;

public interface IDownloadedFileRepository
{
    Task<CachedDownloadedFile?> FindByFileIdAsync(int fileId);
    Task<IReadOnlyList<CachedDownloadedFile>> GetByMessageIdAsync(int messageId);
    Task UpsertAsync(CachedDownloadedFile entry);
    Task DeleteByFileIdAsync(int fileId);
    Task DeleteOrphanedAsync();
}