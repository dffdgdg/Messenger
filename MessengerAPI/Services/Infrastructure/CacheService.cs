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

public partial class CacheService(IMemoryCache cache, ILogger<CacheService> logger) : ICacheService
{
    private static readonly TimeSpan UserChatsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MembershipTtl = TimeSpan.FromMinutes(10);

    #region User Chats

    public async Task<List<int>> GetUserChatIdsAsync(int userId, Func<Task<List<int>>> factory)
    {
        var cacheKey = GetUserChatsKey(userId);

        if (cache.TryGetValue(cacheKey, out List<int>? cachedIds) && cachedIds != null)
        {
            LogUserChatsHit(userId);
            return cachedIds;
        }

        LogUserChatsMiss(userId);

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
            LogMembershipHit(userId, chatId);
            return cached;
        }

        LogMembershipMiss(userId, chatId);


        cache.Set(cacheKey, await factory(), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = MembershipTtl,
            SlidingExpiration = TimeSpan.FromMinutes(3)
        });

        return await factory();
    }

    #endregion

    #region Invalidation

    public void InvalidateUserChats(int userId)
    {
        cache.Remove(GetUserChatsKey(userId));
        LogUserChatsInvalidated(userId);
    }

    public void InvalidateMembership(int userId, int chatId)
    {
        cache.Remove(GetMembershipKey(userId, chatId));
        InvalidateUserChats(userId);
        LogMembershipInvalidated(userId, chatId);
    }

    public void InvalidateChat(int chatId)
    {
        cache.Remove($"chat_{chatId}");
        LogChatInvalidated(chatId);
    }

    public void InvalidateChatMembers(int chatId) => LogChatMembersChanged(chatId);

    #endregion

    #region Key Generation

    private static string GetUserChatsKey(int userId) => $"user_chats_{userId}";
    private static string GetMembershipKey(int userId, int chatId) => $"membership_{userId}_{chatId}";

    #endregion

    #region Log messages

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache HIT: user_chats_{UserId}")]
    private partial void LogUserChatsHit(int userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache MISS: user_chats_{UserId}")]
    private partial void LogUserChatsMiss(int userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache HIT: membership_{UserId}_{ChatId}")]
    private partial void LogMembershipHit(int userId, int chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache MISS: membership_{UserId}_{ChatId}")]
    private partial void LogMembershipMiss(int userId, int chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache invalidated: user_chats_{UserId}")]
    private partial void LogUserChatsInvalidated(int userId);

    [LoggerMessage(Level = LogLevel.Debug,Message = "Cache invalidated: membership_{UserId}_{ChatId}")]
    private partial void LogMembershipInvalidated(int userId, int chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache invalidated: chat_{ChatId}")]
    private partial void LogChatInvalidated(int chatId);

    [LoggerMessage(Level = LogLevel.Debug,Message = "Chat {ChatId} members changed - related caches will expire naturally")]
    private partial void LogChatMembersChanged(int chatId);

    #endregion
}