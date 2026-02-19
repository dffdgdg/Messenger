// File: MessengerDesktop/Data/Entities/CachedReadPointer.cs

using SQLite;

namespace MessengerDesktop.Data.Entities;

[Table("read_pointers")]
public class CachedReadPointer
{
    [PrimaryKey]
    [Column("chat_id")]
    public int ChatId { get; set; }

    [Column("last_read_message_id")]
    public int? LastReadMessageId { get; set; }

    [Column("first_unread_message_id")]
    public int? FirstUnreadMessageId { get; set; }

    [Column("unread_count")]
    public int UnreadCount { get; set; }

    [Column("last_read_at")]
    public long? LastReadAtTicks { get; set; }
}