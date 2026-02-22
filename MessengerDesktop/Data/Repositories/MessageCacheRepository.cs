using MessengerDesktop.Data.Entities;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MessengerDesktop.Data.Repositories;

public class MessageCacheRepository(LocalDatabase localDb) : IMessageCacheRepository
{
    private readonly LocalDatabase _localDb = localDb ?? throw new ArgumentNullException(nameof(localDb));
    private SQLiteAsyncConnection Db => _localDb.Connection;

    public async Task UpsertAsync(CachedMessage message) => await Db.InsertOrReplaceAsync(message);

    public async Task UpsertBatchAsync(IReadOnlyList<CachedMessage> messages)
    {
        if (messages.Count == 0) return;

        var sw = Stopwatch.StartNew();

        await Db.RunInTransactionAsync(conn =>
        {
            foreach (var msg in messages)
            {
                conn.InsertOrReplace(msg);
            }
        });

        sw.Stop();
        Debug.WriteLine($"[MsgCache] Upserted {messages.Count} messages in {sw.ElapsedMilliseconds}ms");
    }

    public async Task MarkDeletedAsync(int messageId) =>
        await Db.ExecuteAsync(
            "UPDATE messages SET is_deleted = 1, content = NULL, poll_json = NULL, files_json = NULL WHERE id = ?",
            messageId);

    public async Task<List<CachedMessage>> GetLatestAsync(int chatId, int count)
    {
        // Индекс idx_msg_chat_id (chat_id, id DESC) — мгновенно
        var messages = await Db.QueryAsync<CachedMessage>(
            @"SELECT * FROM messages 
              WHERE chat_id = ? 
              ORDER BY id DESC 
              LIMIT ?",
            chatId, count);

        // Разворачиваем: UI ожидает хронологический порядок (старые → новые)
        messages.Reverse();
        return messages;
    }

    public async Task<List<CachedMessage>> GetBeforeAsync(int chatId, int beforeId, int count)
    {
        var messages = await Db.QueryAsync<CachedMessage>(
            @"SELECT * FROM messages 
              WHERE chat_id = ? AND id < ? 
              ORDER BY id DESC 
              LIMIT ?",
            chatId, beforeId, count);

        messages.Reverse();
        return messages;
    }

    public async Task<List<CachedMessage>> GetAfterAsync(int chatId, int afterId, int count)
    {
        // Порядок ASC — уже хронологический
        return await Db.QueryAsync<CachedMessage>(
            @"SELECT * FROM messages 
              WHERE chat_id = ? AND id > ? 
              ORDER BY id ASC 
              LIMIT ?",
            chatId, afterId, count);
    }

    public async Task<List<CachedMessage>> GetAroundAsync(int chatId, int messageId, int halfCount)
    {
        // Часть 1: target + до него
        var before = await Db.QueryAsync<CachedMessage>(@"SELECT * FROM messages 
              WHERE chat_id = ? AND id <= ? 
              ORDER BY id DESC 
              LIMIT ?",
            chatId, messageId, halfCount + 1);

        // Часть 2: после target
        var after = await Db.QueryAsync<CachedMessage>(
            @"SELECT * FROM messages 
              WHERE chat_id = ? AND id > ? 
              ORDER BY id ASC 
              LIMIT ?",
            chatId, messageId, halfCount);

        // Собираем в хронологическом порядке
        before.Reverse();
        before.AddRange(after);
        return before;
    }

    public async Task<int?> GetNewestIdAsync(int chatId)
    {
        // ExecuteScalarAsync<int> вернёт 0 если нет записей, поэтому проверяем отдельно
        var count = await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messages WHERE chat_id = ?", chatId);

        if (count == 0) return null;

        return await Db.ExecuteScalarAsync<int>("SELECT MAX(id) FROM messages WHERE chat_id = ?", chatId);
    }

    public async Task<int?> GetOldestIdAsync(int chatId)
    {
        var count = await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messages WHERE chat_id = ?", chatId);

        if (count == 0) return null;

        return await Db.ExecuteScalarAsync<int>("SELECT MIN(id) FROM messages WHERE chat_id = ?", chatId);
    }

    public async Task<int> GetCountAsync(int chatId)
        => await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messages WHERE chat_id = ?", chatId);

    public async Task<int> GetTotalCountAsync()
        => await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messages");

    public async Task<List<CachedMessage>> SearchAsync(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // FTS5 MATCH. Добавляем * для prefix search
        var ftsQuery = query.Trim().Replace("\"", "\"\"") + "*";

        try
        {
            return await Db.QueryAsync<CachedMessage>(
                @"SELECT m.* FROM messages m
                  INNER JOIN messages_fts fts ON m.id = fts.rowid
                  WHERE messages_fts MATCH ?
                  AND m.is_deleted = 0
                  ORDER BY m.id DESC
                  LIMIT ?",
                $"\"{ftsQuery}\"", limit);
        }
        catch (SQLiteException ex)
        {
            // FTS ошибка синтаксиса — fallback на LIKE
            Debug.WriteLine($"[MsgCache] FTS search failed, falling back to LIKE: {ex.Message}");

            return await Db.QueryAsync<CachedMessage>(
                @"SELECT * FROM messages 
                  WHERE content LIKE ? AND is_deleted = 0
                  ORDER BY id DESC LIMIT ?",
                $"%{query}%", limit);
        }
    }

    public async Task DeleteForChatAsync(int chatId)
    {
        var deleted = await Db.ExecuteAsync("DELETE FROM messages WHERE chat_id = ?", chatId);

        Debug.WriteLine($"[MsgCache] Deleted {deleted} messages for chat {chatId}");
    }
}