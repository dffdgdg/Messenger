using MessengerDesktop.Data.Entities;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MessengerDesktop.Data.Repositories;

public class ChatCacheRepository(LocalDatabase localDb) : IChatCacheRepository
{
    private readonly LocalDatabase _localDb = localDb ?? throw new ArgumentNullException(nameof(localDb));
    private SQLiteAsyncConnection Db => _localDb.Connection;

    public async Task UpsertAsync(CachedChat chat) => await Db.InsertOrReplaceAsync(chat);

    public async Task UpsertBatchAsync(IReadOnlyList<CachedChat> chats)
    {
        if (chats.Count == 0) return;

        await Db.RunInTransactionAsync(conn =>
        {
            foreach (var chat in chats)
            {
                conn.InsertOrReplace(chat);
            }
        });

        Debug.WriteLine($"[ChatCache] Upserted {chats.Count} chats");
    }

    public async Task<List<CachedChat>> GetByTypeAsync(int[] chatTypes)
    {
        if (chatTypes.Length == 0) return [];

        // Строим IN clause
        var placeholders = string.Join(",", chatTypes);
        return await Db.QueryAsync<CachedChat>($"SELECT * FROM chats WHERE type IN ({placeholders}) ORDER BY last_message_date DESC");
    }

    public async Task<CachedChat?> GetByIdAsync(int chatId) => await Db.FindAsync<CachedChat>(chatId);

    public async Task UpdateLastMessageAsync(int chatId, string? preview, string? senderName, long dateTicks)
    {
        await Db.ExecuteAsync(
            "UPDATE chats SET last_message_preview = ?, last_message_sender_name = ?, last_message_date = ? WHERE id = ?",
            preview, senderName, dateTicks, chatId);
    }

    public async Task DeleteAsync(int chatId) => await Db.ExecuteAsync("DELETE FROM chats WHERE id = ?", chatId);

    public async Task<int> GetCountAsync() => await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chats");
}