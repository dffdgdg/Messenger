using Desktop.Data.Repositories.Abstractions;

namespace Desktop.ViewModels.ChatList.Factories;

/// <summary>
/// Сервисы локального кэширования чата.
/// </summary>
public sealed class CacheServices(ILocalCacheService cacheService)
{
    public ILocalCacheService CacheService { get; } = cacheService;
}