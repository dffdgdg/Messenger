using Core.Data.Models.Cache;
using Core.Data.Repositories.Abstractions;
using System.Diagnostics;

namespace Core.Data.Repositories.Implementations;

public sealed class DownloadedFileRepository(LocalDatabase db) : IDownloadedFileRepository
{
    public async Task<CachedDownloadedFile?> FindByFileIdAsync(int fileId)
    {
        var conn = db.Connection;
        return await conn.Table<CachedDownloadedFile>().Where(f => f.FileId == fileId).FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<CachedDownloadedFile>> GetByMessageIdAsync(int messageId)
    {
        var conn = db.Connection;
        var rows = await conn.Table<CachedDownloadedFile>().Where(f => f.MessageId == messageId).ToListAsync();
        return rows;
    }

    public async Task UpsertAsync(CachedDownloadedFile entry)
    {
        var conn = db.Connection;
        await conn.InsertOrReplaceAsync(entry);
    }

    public async Task DeleteByFileIdAsync(int fileId)
    {
        var conn = db.Connection;
        await conn.DeleteAsync<CachedDownloadedFile>(fileId);
    }

    public async Task DeleteOrphanedAsync()
    {
        var conn = db.Connection;
        var all = await conn.Table<CachedDownloadedFile>().ToListAsync();

        var orphanIds = all.Where(f => !File.Exists(f.LocalPath)).Select(f => f.FileId).ToList();

        if (orphanIds.Count == 0) return;

        foreach (var id in orphanIds)
            await conn.DeleteAsync<CachedDownloadedFile>(id);

        Debug.WriteLine($"[DownloadedFileRepo] Removed {orphanIds.Count} orphaned entries");
    }
}