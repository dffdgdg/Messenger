using MessengerAPI.Model;
using Microsoft.Extensions.Caching.Memory;

namespace MessengerAPI.Services.Infrastructure;

public interface ICacheService
{
    Task<List<int>> GetUserChatIdsAsync(int userId, Func<Task<List<int>>> factory);
    Task<ChatMember?> GetMembershipAsync(int userId, int chatId, Func<Task<ChatMember?>> factory);
    void InvalidateUserChats(int userId);
    void InvalidateMembership(int userId, int chatId);
    void InvalidateChat(int chatId);
    void InvalidateChatMembers(int chatId);
}

public class CacheService(IMemoryCache cache, ILogger<CacheService> logger) : ICacheService
{
    // Время жизни кэша
    private static readonly TimeSpan UserChatsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MembershipTtl = TimeSpan.FromMinutes(10);

    #region User Chats

    public async Task<List<int>> GetUserChatIdsAsync(int userId, Func<Task<List<int>>> factory)
    {
        var cacheKey = GetUserChatsKey(userId);

        if (cache.TryGetValue(cacheKey, out List<int>? cachedIds) && cachedIds != null)
        {
            logger.LogDebug("Cache HIT: user_chats_{UserId}", userId);
            return cachedIds;
        }

        logger.LogDebug("Cache MISS: user_chats_{UserId}", userId);

        var chatIds = await factory();

        cache.Set(cacheKey, chatIds, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = UserChatsTtl,
            SlidingExpiration = TimeSpan.FromMinutes(2)
        });

        return chatIds;
    }

    #endregion

    #region Membership

    public async Task<ChatMember?> GetMembershipAsync(int userId, int chatId, Func<Task<ChatMember?>> factory)
    {
        var cacheKey = GetMembershipKey(userId, chatId);

        if (cache.TryGetValue(cacheKey, out ChatMember? cached))
        {
            logger.LogDebug("Cache HIT: membership_{UserId}_{ChatId}", userId, chatId);
            return cached;
        }

        logger.LogDebug("Cache MISS: membership_{UserId}_{ChatId}", userId, chatId);

        var member = await factory();

        // Кэшируем даже null (отсутствие членства)
        cache.Set(cacheKey, member, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = MembershipTtl,
            SlidingExpiration = TimeSpan.FromMinutes(3)
        });

        return member;
    }

    #endregion

    #region Invalidation

    public void InvalidateUserChats(int userId)
    {
        var key = GetUserChatsKey(userId);
        cache.Remove(key);
        logger.LogDebug("Cache invalidated: user_chats_{UserId}", userId);
    }

    public void InvalidateMembership(int userId, int chatId)
    {
        var key = GetMembershipKey(userId, chatId);
        cache.Remove(key);
        InvalidateUserChats(userId);
        logger.LogDebug("Cache invalidated: membership_{UserId}_{ChatId}", userId, chatId);
    }

    public void InvalidateChat(int chatId)
    {
        // При удалении чата инвалидируем ключ чата
        var key = $"chat_{chatId}";
        cache.Remove(key);
        logger.LogDebug("Cache invalidated: chat_{ChatId}", chatId);
    }

    public void InvalidateChatMembers(int chatId)
    {
        // Этот метод вызывается при добавлении/удалении участников
        // К сожалению, IMemoryCache не поддерживает удаление по паттерну,
        // поэтому инвалидация членства происходит по конкретным ключам
        logger.LogDebug("Chat {ChatId} members changed - related caches will expire naturally", chatId);
    }

    #endregion

    #region Key Generation

    private static string GetUserChatsKey(int userId) => $"user_chats_{userId}";
    private static string GetMembershipKey(int userId, int chatId) => $"membership_{userId}_{chatId}";

    #endregion
}