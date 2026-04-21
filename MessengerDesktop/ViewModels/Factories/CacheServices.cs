using MessengerDesktop.Data.Repositories;

namespace MessengerDesktop.ViewModels.Factories;

/// <summary>
/// Сервисы локального кэширования чата.
/// </summary>
public sealed class CacheServices(ILocalCacheService cacheService)
{
    public ILocalCacheService CacheService { get; } = cacheService;
}