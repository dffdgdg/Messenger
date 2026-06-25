using API.Application.Services.Abstractions;
using API.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace API.Infrastructure.Cache;

public sealed partial class CacheService(IMemoryCache cache, ILogger<CacheService> logger)
    : ICacheService
{
    private static readonly TimeSpan UserChatsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MembershipTtl = TimeSpan.FromMinutes(10);

    #region User chats

    public async Task<List<int>> GetUserChatIdsAsync(int userId, Func<Task<List<int>>> factory)
    {
        var key = GetUserChatsKey(userId);

        if (cache.TryGetValue(key, out List<int>? cached) && cached != null)
        {
            LogUserChatsHit(userId);
            return cached;
        }

        LogUserChatsMiss(userId);

        var chatIds = await factory();

        cache.Set(key, chatIds, new MemoryCacheEntryOptions
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
        var key = GetMembershipKey(userId, chatId);

        if (cache.TryGetValue(key, out ChatMember? cached))
        {
            LogMembershipHit(userId, chatId);
            return cached;
        }

        LogMembershipMiss(userId, chatId);

        var member = await factory();

        cache.Set(key, member, new MemoryCacheEntryOptions
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

    #endregion

    #region Keys

    private static string GetUserChatsKey(int userId) => $"user_chats_{userId}";
    private static string GetMembershipKey(int userId, int chatId)
        => $"membership_{userId}_{chatId}";

    #endregion

    #region Logging

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache invalidated: membership_{UserId}_{ChatId}")]
    private partial void LogMembershipInvalidated(int userId, int chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache invalidated: chat_{ChatId}")]
    private partial void LogChatInvalidated(int chatId);

    #endregion
}