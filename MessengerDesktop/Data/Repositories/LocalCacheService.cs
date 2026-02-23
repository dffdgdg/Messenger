using MessengerDesktop.Data.Entities;
using MessengerDesktop.Data.Mappers;
using MessengerShared.DTO.Chat;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.User;
using MessengerShared.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.Data.Repositories;

public class LocalCacheService(LocalDatabase localDb,IMessageCacheRepository messageRepo,IChatCacheRepository chatRepo)
    : ILocalCacheService
{
    private readonly IMessageCacheRepository _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
    private readonly IChatCacheRepository _chatRepo = chatRepo ?? throw new ArgumentNullException(nameof(chatRepo));
    private readonly LocalDatabase _localDb = localDb ?? throw new ArgumentNullException(nameof(localDb));

    // ═══════════════════════════════════════════════════════
    //  Messages
    // ═══════════════════════════════════════════════════════

    public async Task UpsertMessageAsync(MessageDTO message)
    {
        var entity = message.ToEntity();
        await _messageRepo.UpsertAsync(entity);
    }

    public async Task UpsertMessagesAsync(IEnumerable<MessageDTO> messages)
    {
        var entities = messages.Select(m => m.ToEntity()).ToList();
        if (entities.Count == 0) return;
        await _messageRepo.UpsertBatchAsync(entities);
    }

    public async Task MarkMessageDeletedAsync(int messageId)
        => await _messageRepo.MarkDeletedAsync(messageId);

    public async Task<CachedMessagesResult?> GetMessagesAsync(int chatId, int count)
    {
        var syncState = await GetSyncStateAsync(chatId);
        if (syncState == null) return null; // Нет sync state = кэш пуст для этого чата

        var cached = await _messageRepo.GetLatestAsync(chatId, count);
        if (cached.Count == 0) return null;

        return new CachedMessagesResult
        {
            Messages = cached.ConvertAll(m => m.ToDto()),
            HasMoreOlder = syncState.HasMoreOlder,
            HasMoreNewer = syncState.HasMoreNewer,
            IsComplete = cached.Count >= count,
            CacheOldestId = syncState.OldestLoadedId,
            CacheNewestId = syncState.NewestLoadedId
        };
    }

    public async Task<CachedMessagesResult?> GetMessagesBeforeAsync(int chatId, int beforeId, int count)
    {
        var cached = await _messageRepo.GetBeforeAsync(chatId, beforeId, count);
        if (cached.Count == 0) return null;

        var syncState = await GetSyncStateAsync(chatId);

        // IsComplete = кэш покрывает весь запрос
        // либо это самое старое что есть и сервер тоже пуст
        var isComplete = cached.Count >= count
            || (syncState is { HasMoreOlder: false }
                && cached.Count > 0
                && cached[0].Id == (syncState.OldestLoadedId ?? 0));

        return new CachedMessagesResult
        {
            Messages = cached.ConvertAll(m => m.ToDto()),
            HasMoreOlder = syncState?.HasMoreOlder ?? true,
            HasMoreNewer = true, // Если мы смотрим "before" — newer точно есть
            IsComplete = isComplete,
            CacheOldestId = syncState?.OldestLoadedId,
            CacheNewestId = syncState?.NewestLoadedId
        };
    }

    public async Task<CachedMessagesResult?> GetMessagesAfterAsync(int chatId, int afterId, int count)
    {
        var cached = await _messageRepo.GetAfterAsync(chatId, afterId, count);
        if (cached.Count == 0) return null;

        var syncState = await GetSyncStateAsync(chatId);

        return new CachedMessagesResult
        {
            Messages = cached.ConvertAll(m => m.ToDto()),
            HasMoreOlder = true,
            HasMoreNewer = syncState?.HasMoreNewer ?? true,
            IsComplete = cached.Count >= count,
            CacheOldestId = syncState?.OldestLoadedId,
            CacheNewestId = syncState?.NewestLoadedId
        };
    }

    public async Task<CachedMessagesResult?> GetMessagesAroundAsync(int chatId, int messageId, int count)
    {
        var halfCount = count / 2;
        var cached = await _messageRepo.GetAroundAsync(chatId, messageId, halfCount);
        if (cached.Count == 0) return null;

        var syncState = await GetSyncStateAsync(chatId);

        // IsComplete = target message найден в кэше
        var hasTarget = cached.Any(m => m.Id == messageId);

        return new CachedMessagesResult
        {
            Messages = cached.ConvertAll(m => m.ToDto()),
            HasMoreOlder = syncState?.HasMoreOlder ?? true,
            HasMoreNewer = syncState?.HasMoreNewer ?? true,
            IsComplete = hasTarget,
            CacheOldestId = syncState?.OldestLoadedId,
            CacheNewestId = syncState?.NewestLoadedId
        };
    }

    public async Task<List<MessageDTO>> SearchMessagesLocalAsync(string query, int limit = 50)
    {
        var cached = await _messageRepo.SearchAsync(query, limit);
        return cached.ConvertAll(m => m.ToDto());
    }

    // ═══════════════════════════════════════════════════════
    //  Chats
    // ═══════════════════════════════════════════════════════

    public async Task<List<ChatDTO>> GetChatsAsync(bool isGroupMode)
    {
        int[] typeFilter = isGroupMode
            ? [(int)ChatType.Chat, (int)ChatType.Department]
            : [(int)ChatType.Contact];

        var cached = await _chatRepo.GetByTypeAsync(typeFilter);
        return cached.ConvertAll(c => c.ToDto());
    }

    public async Task UpsertChatsAsync(IEnumerable<ChatDTO> chats)
    {
        var entities = chats.Select(c => c.ToEntity()).ToList();
        if (entities.Count == 0) return;
        await _chatRepo.UpsertBatchAsync(entities);
    }

    public async Task UpdateChatLastMessageAsync(int chatId, string? preview, string? senderName, DateTime date)
    {
        var dateTicks = date.ToUniversalTime().Ticks;
        await _chatRepo.UpdateLastMessageAsync(chatId, preview, senderName, dateTicks);
    }

    // ═══════════════════════════════════════════════════════
    //  Sync State
    // ═══════════════════════════════════════════════════════

    public async Task<ChatSyncState?> GetSyncStateAsync(int chatId)
        => await _localDb.Connection.FindAsync<ChatSyncState>(chatId);

    public async Task UpdateSyncStateAsync(ChatSyncState state)
    {
        state.LastSyncAtTicks = DateTime.UtcNow.Ticks;
        await _localDb.Connection.InsertOrReplaceAsync(state);
    }

    // ═══════════════════════════════════════════════════════
    //  Read Pointers
    // ═══════════════════════════════════════════════════════

    public async Task UpdateReadPointerAsync(int chatId, int? lastReadMessageId, int unreadCount)
    {
        var pointer = new CachedReadPointer
        {
            ChatId = chatId,
            LastReadMessageId = lastReadMessageId,
            UnreadCount = unreadCount,
            LastReadAtTicks = DateTime.UtcNow.Ticks
        };
        await _localDb.Connection.InsertOrReplaceAsync(pointer);
    }

    public async Task<CachedReadPointer?> GetReadPointerAsync(int chatId)
        => await _localDb.Connection.FindAsync<CachedReadPointer>(chatId);

    // ═══════════════════════════════════════════════════════
    //  Users
    // ═══════════════════════════════════════════════════════

    public async Task UpsertUsersAsync(IEnumerable<UserDTO> users)
    {
        var entities = users.Select(u => u.ToEntity()).ToList();
        if (entities.Count == 0) return;

        await _localDb.Connection.RunInTransactionAsync(conn =>
        {
            foreach (var e in entities)
            {
                conn.InsertOrReplace(e);
            }
        });
    }

    // ═══════════════════════════════════════════════════════
    //  Maintenance
    // ═══════════════════════════════════════════════════════
    public async Task ClearAllAsync() => await _localDb.ClearAllAsync();

    public async Task<long> GetDatabaseSizeBytesAsync() => await _localDb.GetDatabaseSizeBytesAsync();
}