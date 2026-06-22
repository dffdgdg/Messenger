using Core.Data.Mappers;
using Core.Data.Mapping;
using Core.Data.Models.Cache;
using Core.Data.Models.Sync;
using Core.Data.Repositories.Abstractions;
using System.Diagnostics;
using System.Text.Json;

namespace Core.Data.Repositories.Implementations;

public class LocalCacheService(LocalDatabase localDb, IMessageCacheRepository messageRepo, IChatCacheRepository chatRepo)
    : ILocalCacheService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = CacheJsonContext.Default
    };

    private readonly IMessageCacheRepository _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
    private readonly IChatCacheRepository _chatRepo = chatRepo ?? throw new ArgumentNullException(nameof(chatRepo));
    private readonly LocalDatabase _localDb = localDb ?? throw new ArgumentNullException(nameof(localDb));

    public async Task ClearChatMessagesAsync(int chatId)
    {
        await _messageRepo.DeleteByChatIdAsync(chatId);
        await _localDb.Connection.DeleteAsync<ChatSyncState>(chatId);
    }
    public async Task UpsertMessageAsync(MessageDto message)
    {
        var entity = message.ToEntity();
        await _messageRepo.UpsertAsync(entity);
    }

    public async Task UpdatePollThreadAsync(PollDto poll)
    {
        var pollJson = JsonSerializer.Serialize(poll, JsonOpts);
        await _messageRepo.UpdatePollThreadAsync(poll.MessageId, pollJson);
    }

    public async Task UpsertMessagesAsync(IEnumerable<MessageDto> messages)
    {
        var entities = messages.Select(m => m.ToEntity()).ToList();
        if (entities.Count == 0) return;
        await _messageRepo.UpsertBatchAsync(entities);
    }

    public async Task MarkMessageDeletedAsync(int messageId) => await _messageRepo.MarkDeletedAsync(messageId);

    public async Task<CachedMessagesResult?> GetMessagesAsync(int chatId, int count) => await Task.Run(async () =>
    {
        var syncState = await GetSyncStateAsync(chatId);
        if (syncState == null) return null;

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
    });

    public async Task<CachedMessagesResult?> GetMessagesBeforeAsync(
    int chatId, int beforeId, int count) => await Task.Run(async () =>
    {
        var cached = await _messageRepo.GetBeforeAsync(chatId, beforeId, count);
        if (cached.Count == 0) return null;

        var syncState = await GetSyncStateAsync(chatId);

        // Кэш полон если:
        // 1. Вернули запрошенное количество, ИЛИ
        // 2. Достигли начала истории (HasMoreOlder=false)
        bool reachedHistoryStart = syncState is { HasMoreOlder: false }
            && syncState.OldestLoadedId.HasValue
            && cached.Any(m => m.Id <= syncState.OldestLoadedId.Value);

        var isComplete = cached.Count >= count || reachedHistoryStart;

        return new CachedMessagesResult
        {
            Messages = cached.ConvertAll(m => m.ToDto()),
            HasMoreOlder = !reachedHistoryStart && (syncState?.HasMoreOlder ?? true),
            HasMoreNewer = true,
            IsComplete = isComplete,
            CacheOldestId = syncState?.OldestLoadedId,
            CacheNewestId = syncState?.NewestLoadedId
        };
    });

    public async Task<CachedMessagesResult?> GetMessagesAfterAsync(int chatId, int afterId, int count) => await Task.Run(async () =>
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
    });

    public async Task<CachedMessagesResult?> GetMessagesAroundAsync(int chatId, int messageId, int count) => await Task.Run(async () =>
    {
        var halfCount = count / 2;
        var cached = await _messageRepo.GetAroundAsync(chatId, messageId, halfCount);
        if (cached.Count == 0) return null;

        foreach (var m in cached.Where(m => m.PollJson != null))
            Debug.WriteLine($"[Cache.Around] msg={m.Id} pollJson={m.PollJson}");
        var syncState = await GetSyncStateAsync(chatId);
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
    });

    public async Task<List<ChatDto>> GetChatsAsync(bool isGroupMode)
    {
        int[] typeFilter = isGroupMode ? [(int)ChatType.Chat, (int)ChatType.Department] : [(int)ChatType.Contact];

        var cached = await _chatRepo.GetByTypeAsync(typeFilter);
        return cached.ConvertAll(c => c.ToDto());
    }

    public async Task UpsertChatsAsync(IEnumerable<ChatDto> chats)
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

    public async Task<ChatSyncState?> GetSyncStateAsync(int chatId)
        => await _localDb.Connection.FindAsync<ChatSyncState>(chatId);

    public async Task UpdateSyncStateAsync(ChatSyncState state)
    {
        state.LastSyncAtTicks = DateTime.UtcNow.Ticks;
        await _localDb.Connection.InsertOrReplaceAsync(state);
    }

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
    public async Task PatchChatMetaAsync(ChatUpdateEventDto update)
    {
        await _localDb.Connection.ExecuteAsync(
            """
        UPDATE chats
        SET name = ?,
            avatar = ?,
            show_history_for_new_members = ?
        WHERE id = ?
        """,
            update.Name,
            update.Avatar,
            update.ShowHistoryForNewMembers ? 1 : 0,
            update.Id);
    }

    public async Task<CachedReadPointer?> GetReadPointerAsync(int chatId)
        => await _localDb.Connection.FindAsync<CachedReadPointer>(chatId);

    public async Task UpsertUsersAsync(IEnumerable<UserDto> users)
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
    public async Task TrimOldMessagesAsync(int keepPerChat = 200)
        => await _messageRepo.TrimOldMessagesAsync(keepPerChat);
    public async Task ClearAllAsync() => await _localDb.ClearAllAsync();

    public async Task<long> GetDatabaseSizeBytesAsync() => await _localDb.GetDatabaseSizeBytesAsync();

}