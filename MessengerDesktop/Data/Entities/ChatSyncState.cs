using SQLite;
using System;

namespace MessengerDesktop.Data.Entities;

[Table("chat_sync_state")]
public class ChatSyncState
{
    [PrimaryKey][Column("chat_id")] public int ChatId { get; set; }
    [Column("oldest_loaded_id")] public int? OldestLoadedId { get; set; }
    [Column("newest_loaded_id")] public int? NewestLoadedId { get; set; }
    [Column("has_more_older")] public bool HasMoreOlder { get; set; } = true;
    [Column("has_more_newer")] public bool HasMoreNewer { get; set; }
    [Column("last_sync_at")] public long LastSyncAtTicks { get; set; }
    [Ignore] public DateTime LastSyncAt => new(LastSyncAtTicks, DateTimeKind.Utc);
}