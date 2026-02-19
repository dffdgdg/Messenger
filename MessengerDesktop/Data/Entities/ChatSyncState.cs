// File: MessengerDesktop/Data/Entities/ChatSyncState.cs

using SQLite;
using System;

namespace MessengerDesktop.Data.Entities;

[Table("chat_sync_state")]
public class ChatSyncState
{
    [PrimaryKey]
    [Column("chat_id")]
    public int ChatId { get; set; }

    /// <summary>Самый старый ID сообщения, загруженный в кэш для этого чата</summary>
    [Column("oldest_loaded_id")]
    public int? OldestLoadedId { get; set; }

    /// <summary>Самый новый ID сообщения, загруженный в кэш для этого чата</summary>
    [Column("newest_loaded_id")]
    public int? NewestLoadedId { get; set; }

    /// <summary>Есть ли на сервере ещё более старые сообщения</summary>
    [Column("has_more_older")]
    public bool HasMoreOlder { get; set; } = true;

    /// <summary>Есть ли на сервере более новые сообщения</summary>
    [Column("has_more_newer")]
    public bool HasMoreNewer { get; set; }

    [Column("last_sync_at")]
    public long LastSyncAtTicks { get; set; }

    [Ignore] public DateTime LastSyncAt => new(LastSyncAtTicks, DateTimeKind.Utc);
}