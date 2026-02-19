using MessengerDesktop.Data;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Cache;

/// <summary>
/// Обслуживание локального кэша:
/// - Очистка старых данных
/// - Контроль размера БД
/// - VACUUM после массовых удалений
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

    // ── Лимиты ──
    private const int MaxTotalMessages = 50_000;
    private const long MaxDbSizeBytes = 100 * 1024 * 1024; // 100 MB
    private static readonly TimeSpan MaxMessageAge = TimeSpan.FromDays(30);

    public async Task RunMaintenanceAsync()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Удаляем старые сообщения по возрасту и лимиту
            await _cache.CleanupOldDataAsync(MaxMessageAge, MaxTotalMessages);

            // 2. Проверяем размер БД
            var dbSize = await _cache.GetDatabaseSizeBytesAsync();
            Debug.WriteLine($"[Maintenance] DB size: {dbSize / 1024}KB");

            if (dbSize > MaxDbSizeBytes)
            {
                Debug.WriteLine($"[Maintenance] DB too large ({dbSize / 1024 / 1024}MB), aggressive cleanup");

                // Агрессивная очистка: оставляем только 7 дней и половину лимита
                await _cache.CleanupOldDataAsync(TimeSpan.FromDays(7), MaxTotalMessages / 2);
                await _localDb.VacuumAsync();
            }

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