using MessengerDesktop.Data;
using MessengerDesktop.Data.Repositories;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Cache;

/// <summary>
/// Обслуживание локального кэша.
/// </summary>
public interface ICacheMaintenanceService
{
    /// <summary>Запустить обслуживание (вызывать при старте + периодически)</summary>
    Task RunMaintenanceAsync();

    /// <summary>Полная очистка кэша (логаут / смена пользователя)</summary>
    Task ClearAllDataAsync();

    /// <summary>Очистка кэша для конкретного чата</summary>
    Task ClearChatCacheAsync(int chatId);
}

public class CacheMaintenanceService(ILocalCacheService cache, LocalDatabase localDb) : ICacheMaintenanceService
{
    private readonly ILocalCacheService _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly LocalDatabase _localDb = localDb ?? throw new ArgumentNullException(nameof(localDb));

    public async Task RunMaintenanceAsync()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var dbSize = await _cache.GetDatabaseSizeBytesAsync();
            Debug.WriteLine($"[Maintenance] DB size: {dbSize / 1024}KB");
            sw.Stop();
            Debug.WriteLine($"[Maintenance] Completed in {sw.ElapsedMilliseconds}ms");
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
            await _cache.UpdateSyncStateAsync(new Data.Entities.ChatSyncState
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