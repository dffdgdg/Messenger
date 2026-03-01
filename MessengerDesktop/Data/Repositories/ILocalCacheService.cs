using MessengerDesktop.Data.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MessengerDesktop.Data.Repositories;

/// <summary>
/// Результат чтения сообщений из кэша.
/// Содержит данные + метаинформацию о покрытии кэша.
/// </summary>
public class CachedMessagesResult
{
    /// <summary>Сообщения в хронологическом порядке (старые → новые)</summary>
    public List<MessageDto> Messages { get; init; } = [];

    /// <summary>На сервере есть ещё более старые сообщения</summary>
    public bool HasMoreOlder { get; init; }

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
    // ═══ Messages ═══

    Task UpsertMessageAsync(MessageDto message);
    Task UpsertMessagesAsync(IEnumerable<MessageDto> messages);
    Task MarkMessageDeletedAsync(int messageId);

    /// <summary>
    /// Последние N сообщений чата из кэша.
    /// Возвращает null если для этого чата кэш пуст.
    /// </summary>
    Task<CachedMessagesResult?> GetMessagesAsync(int chatId, int count);

    /// <summary>Сообщения до указанного ID (пагинация вверх)</summary>
    Task<CachedMessagesResult?> GetMessagesBeforeAsync(int chatId, int beforeId, int count);

    /// <summary>Сообщения после указанного ID (gap fill / пагинация вниз)</summary>
    Task<CachedMessagesResult?> GetMessagesAfterAsync(int chatId, int afterId, int count);

    /// <summary>Сообщения вокруг указанного ID (прыжок к сообщению)</summary>
    Task<CachedMessagesResult?> GetMessagesAroundAsync(int chatId, int messageId, int count);

    /// <summary>Полнотекстовый поиск по локальным сообщениям</summary>
    Task<List<MessageDto>> SearchMessagesLocalAsync(string query, int limit = 50);

    // ═══ Chats ═══

    /// <summary>Список чатов из кэша, отфильтрованный по режиму (groups/dialogs)</summary>
    Task<List<ChatDto>> GetChatsAsync(bool isGroupMode);
    Task UpsertChatsAsync(IEnumerable<ChatDto> chats);
    Task UpdateChatLastMessageAsync(int chatId, string? preview, string? senderName, DateTime date);

    // ═══ Sync State ═══

    Task<ChatSyncState?> GetSyncStateAsync(int chatId);
    Task UpdateSyncStateAsync(ChatSyncState state);

    // ═══ Read Pointers ═══

    Task UpdateReadPointerAsync(int chatId, int? lastReadMessageId, int unreadCount);
    Task<CachedReadPointer?> GetReadPointerAsync(int chatId);

    // ═══ Users ═══

    Task UpsertUsersAsync(IEnumerable<UserDto> users);

    // ═══ Maintenance ═══

    /// <summary>Полная очистка (логаут / смена пользователя)</summary>
    Task ClearAllAsync();

    /// <summary>Размер БД в байтах</summary>
    Task<long> GetDatabaseSizeBytesAsync();
}