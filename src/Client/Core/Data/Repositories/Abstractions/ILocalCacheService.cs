using Core.Data.Models.Cache;
using Core.Data.Models.Sync;
using Shared.Contracts.Chat;
using Shared.Contracts.Message;
using Shared.Contracts.Poll;
using Shared.Contracts.User;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Data.Repositories.Abstractions;

/// <summary>
/// Результат чтения сообщений из кэша.
/// Содержит данные + метаинформацию о покрытии кэша.
/// </summary>
public class CachedMessagesResult
{
    /// <summary>Сообщения в хронологическом порядке (старые → новые)</summary>
    public List<MessageDto> Messages { get; init; } = [];
    /// <summary>На сервере есть ещё более старые сообщения</summary>
    public bool HasMoreOlder { get; set; }
    /// <summary>На сервере есть более новые сообщения</summary>
    public bool HasMoreNewer { get; init; }
    /// <summary>true если кэш полностью покрывает запрошенный диапазон</summary>
    public bool IsComplete { get; init; }
    /// <summary>Самый старый ID в кэше для этого чата</summary>
    public int? CacheOldestId { get; init; }
    /// <summary>Самый новый ID в кэше для этого чата</summary>
    public int? CacheNewestId { get; init; }
}

/// <summary>
/// Фасад над всеми операциями с локальным кэшем.
/// Работает с DTO (не с entity), скрывает детали хранения.
/// Потокобезопасен.
/// </summary>
public interface ILocalCacheService
{
    Task UpsertMessageAsync(MessageDto message);
    Task UpsertMessagesAsync(IEnumerable<MessageDto> messages);
    Task UpdatePollThreadAsync(PollDto poll);
    Task MarkMessageDeletedAsync(int messageId);
    Task PatchChatMetaAsync(ChatUpdateEventDto update);
    /// <summary>
    /// Используется при gap fill overflow (слишком большой разрыв после reconnect).
    /// </summary>
    Task ClearChatMessagesAsync(int chatId);
    /// <summary>
    /// Последние N сообщений чата из кэша.
    /// </summary>
    Task<CachedMessagesResult?> GetMessagesAsync(int chatId, int count);
    /// <summary>Сообщения до указанного ID (пагинация вверх)</summary>
    Task<CachedMessagesResult?> GetMessagesBeforeAsync(int chatId, int beforeId, int count);
    /// <summary>Сообщения после указанного ID (gap fill / пагинация вниз)</summary>
    Task<CachedMessagesResult?> GetMessagesAfterAsync(int chatId, int afterId, int count);
    /// <summary>Сообщения вокруг указанного ID (прыжок к сообщению)</summary>
    Task<CachedMessagesResult?> GetMessagesAroundAsync(int chatId, int messageId, int count);
    Task<List<ChatDto>> GetChatsAsync(bool isGroupMode);
    Task UpsertChatsAsync(IEnumerable<ChatDto> chats);
    Task UpdateChatLastMessageAsync(int chatId, string? preView, string? senderName, DateTime date);
    Task<ChatSyncState?> GetSyncStateAsync(int chatId);
    Task UpdateSyncStateAsync(ChatSyncState state);
    Task UpdateReadPointerAsync(int chatId, int? lastReadMessageId, int unreadCount);
    Task<CachedReadPointer?> GetReadPointerAsync(int chatId);
    Task UpsertUsersAsync(IEnumerable<UserDto> users);
    /// <summary>Полная очистка (логаут / смена пользователя)</summary>
    Task ClearAllAsync();
    /// <summary>Размер БД в байтах</summary>
    Task<long> GetDatabaseSizeBytesAsync();
    /// <summary> Удаляет старые сообщения, оставляя keepPerChat последних для каждого чата. </summary>
    Task TrimOldMessagesAsync(int keepPerChat = 200);
}