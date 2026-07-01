using Core.Data.Models.Cache;
using Core.Data.Repositories.Abstractions;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Core.Data.Repositories.Implementations;

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
        await Db.ExecuteAsync("""
            UPDATE messages
            SET is_deleted = 1,
                content = NULL,
                poll_json = NULL,
                files_json = NULL,
                reply_to_message_id = NULL,
                reply_sender_name = NULL,
                reply_content_preView = NULL,
                reply_is_deleted = 0,
                reply_sender_id = NULL,
                reply_chat_id = NULL,
                reply_is_voice = 0,
                reply_has_poll = 0,
                reply_files_count = 0,
                forwarded_from_message_id = NULL,
                forward_sender_name = NULL,
                forward_original_sender_id = NULL,
                forward_original_chat_id = NULL,
                forward_original_date = NULL
            WHERE id = ?;

            UPDATE messages
            SET reply_is_deleted = 1,
                reply_content_preView = NULL
            WHERE reply_to_message_id = ?
            """, messageId, messageId);

    public async Task UpdatePollThreadAsync(int originalMessageId, string pollJson)
        => await Db.ExecuteAsync("UPDATE messages SET poll_json = ? WHERE is_deleted = 0 AND (id = ? OR forwarded_from_message_id = ?)",
            pollJson, originalMessageId, originalMessageId);

    public async Task<List<CachedMessage>> GetLatestAsync(int chatId, int count)
    {
        var messages = await Db.QueryAsync<CachedMessage>(
            "SELECT * FROM messages WHERE chat_id = ? ORDER BY id DESC LIMIT ?",
            chatId, count);
        messages.Reverse();
        return messages;
    }

    public async Task<List<CachedMessage>> GetBeforeAsync(int chatId, int beforeId, int count)
    {
        var messages = await Db.QueryAsync<CachedMessage>("SELECT * FROM messages WHERE chat_id = ? AND id < ? ORDER BY id DESC LIMIT ?", chatId, beforeId, count);
        messages.Reverse();
        return messages;
    }

    public async Task<List<CachedMessage>> GetAfterAsync(int chatId, int afterId, int count)
        => await Db.QueryAsync<CachedMessage>("SELECT * FROM messages WHERE chat_id = ? AND id > ? ORDER BY id ASC LIMIT ?", chatId, afterId, count);

    public async Task<List<CachedMessage>> GetAroundAsync(int chatId, int messageId, int halfCount)
    {
        var before = await Db.QueryAsync<CachedMessage>("SELECT * FROM messages WHERE chat_id = ? AND id <= ? ORDER BY id DESC LIMIT ?", chatId, messageId, halfCount + 1);
        var after = await Db.QueryAsync<CachedMessage>("SELECT * FROM messages WHERE chat_id = ? AND id > ? ORDER BY id ASC LIMIT ?", chatId, messageId, halfCount);

        before.Reverse();
        before.AddRange(after);
        return before;
    }

    public async Task<int?> GetNewestIdAsync(int chatId)
    {
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

    public async Task DeleteByChatIdAsync(int chatId)
        => await Db.ExecuteAsync("DELETE FROM messages WHERE chat_id = ?", chatId);

    
    public async Task TrimOldMessagesAsync(int keepPerChat = 200)
    {
        var chatIds = await Db.QueryAsync<ChatIdRow>(
            "SELECT DISTINCT chat_id FROM messages");

        if (chatIds.Count == 0) return;

        // Собираем данные вне транзакции (транзакция только для записи)
        var trimResults = new List<(int ChatId, int CutoffId)>();

        foreach (var row in chatIds)
        {
            var cutoffId = await Db.ExecuteScalarAsync<int?>(
                @"SELECT MIN(id) FROM (
                SELECT id FROM messages 
                WHERE chat_id = ? 
                ORDER BY id DESC 
                LIMIT ?
            )", row.ChatId, keepPerChat);

            if (cutoffId.HasValue)
                trimResults.Add((row.ChatId, cutoffId.Value));
        }

        if (trimResults.Count == 0) return;

        await Db.RunInTransactionAsync(conn =>
        {
            foreach (var (chatId, cutoffId) in trimResults)
            {
                var deleted = conn.Execute(
                    "DELETE FROM messages WHERE chat_id = ? AND id < ?",
                    chatId, cutoffId);

                if (deleted > 0)
                {
                    conn.Execute(
                        @"INSERT INTO chat_sync_state 
                        (chat_id, oldest_loaded_id, has_more_older, has_more_newer, last_sync_at)
                      VALUES (?, ?, 1, 0, ?)
                      ON CONFLICT(chat_id) DO UPDATE SET
                        oldest_loaded_id = excluded.oldest_loaded_id,
                        has_more_older = 1",
                        chatId, cutoffId, DateTime.UtcNow.Ticks);

                    Debug.WriteLine(
                        $"[MsgCache] Trimmed chat {chatId}: deleted {deleted} msgs, " +
                        $"oldest kept = {cutoffId}, set has_more_older = true");
                }
            }
        });

        Debug.WriteLine($"[MsgCache] Trim complete, keeping {keepPerChat} per chat");
    }

    public async Task DeleteForChatAsync(int chatId)
    {
        var deleted = await Db.ExecuteAsync("DELETE FROM messages WHERE chat_id = ?", chatId);

        Debug.WriteLine($"[MsgCache] Deleted {deleted} messages for chat {chatId}");
    }
    private class ChatIdRow
    {
        [Column("chat_id")] public int ChatId { get; set; }
    }
}