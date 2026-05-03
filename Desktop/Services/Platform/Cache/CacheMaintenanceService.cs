using Desktop.Data;
using Desktop.Data.Repositories.Abstractions;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Desktop.Services.Platform.Cache;

public class CacheMaintenanceService(ILocalCacheService cache, LocalDatabase localDb) : ICacheMaintenanceService
{
    private readonly ILocalCacheService _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly LocalDatabase _localDb = localDb ?? throw new ArgumentNullException(nameof(localDb));

    public async Task RunMaintenanceAsync()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var dbSizeBefore = await _cache.GetDatabaseSizeBytesAsync();
            Debug.WriteLine($"[Maintenance] DB size before: {dbSizeBefore / 1024}KB");

            await _cache.TrimOldMessagesAsync(keepPerChat: 200);
            await _localDb.VacuumAsync();

            var dbSizeAfter = await _cache.GetDatabaseSizeBytesAsync();
            sw.Stop();
            Debug.WriteLine($"[Maintenance] DB size after: {dbSizeAfter / 1024}KB, " +
                            $"freed: {(dbSizeBefore - dbSizeAfter) / 1024}KB, " +
                            $"took: {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Maintenance] Error: {ex.Message}");
        }
    }

    public async Task ClearAllDataAsync()
    {
        try
        {
            await _cache.ClearAllAsync();
            await _localDb.VacuumAsync();
            Debug.WriteLine("[Maintenance] All cache data cleared");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Maintenance] ClearAll error: {ex.Message}");
        }
    }

    public async Task ClearChatCacheAsync(int chatId)
    {
        try
        {
            // Удаляем сообщения чата через репозиторий (через cache service)
            // Пока используем существующий API — очищаем sync state
            await _cache.UpdateSyncStateAsync(new Data.Models.Sync.ChatSyncState
            {
                ChatId = chatId,
                OldestLoadedId = null,
                NewestLoadedId = null,
                HasMoreOlder = true,
                HasMoreNewer = false
            });

            Debug.WriteLine($"[Maintenance] Chat {chatId} cache reset");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Maintenance] ClearChat error: {ex.Message}");
        }
    }
}