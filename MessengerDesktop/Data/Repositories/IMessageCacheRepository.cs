using MessengerDesktop.Data.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MessengerDesktop.Data.Repositories;

public interface IMessageCacheRepository
{
    /// <summary>Вставить или обновить одно сообщение</summary>
    Task UpsertAsync(CachedMessage message);

    /// <summary>Вставить или обновить пачку сообщений (в транзакции)</summary>
    Task UpsertBatchAsync(IReadOnlyList<CachedMessage> messages);

    /// <summary>Пометить сообщение как удалённое (мягкое удаление)</summary>
    Task MarkDeletedAsync(int messageId);

    /// <summary>Последние N сообщений чата (от новых к старым → разворачиваем)</summary>
    Task<List<CachedMessage>> GetLatestAsync(int chatId, int count);

    /// <summary>N сообщений до указанного ID (для пагинации вверх)</summary>
    Task<List<CachedMessage>> GetBeforeAsync(int chatId, int beforeId, int count);

    /// <summary>N сообщений после указанного ID (для пагинации вниз / gap fill)</summary>
    Task<List<CachedMessage>> GetAfterAsync(int chatId, int afterId, int count);

    /// <summary>Сообщения вокруг указанного ID (для прыжка к сообщению)</summary>
    Task<List<CachedMessage>> GetAroundAsync(int chatId, int messageId, int halfCount);

    /// <summary>Самый новый ID сообщения в чате</summary>
    Task<int?> GetNewestIdAsync(int chatId);

    /// <summary>Самый старый ID сообщения в чате</summary>
    Task<int?> GetOldestIdAsync(int chatId);

    /// <summary>Количество сообщений в конкретном чате</summary>
    Task<int> GetCountAsync(int chatId);

    /// <summary>Общее количество сообщений во всём кэше</summary>
    Task<int> GetTotalCountAsync();

    /// <summary>Полнотекстовый поиск через FTS5</summary>
    Task<List<CachedMessage>> SearchAsync(string query, int limit);

    /// <summary>Удалить сообщения старше указанной даты</summary>
    Task DeleteOlderThanAsync(DateTime cutoffUtc);

    /// <summary>Удалить все сообщения чата</summary>
    Task DeleteForChatAsync(int chatId);
}