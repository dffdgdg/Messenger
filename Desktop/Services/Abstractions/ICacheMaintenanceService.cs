namespace Desktop.Services.Abstractions;

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
