using SQLite;
using System;

namespace Core.Data.Models.Cache;

[Table("chats")]
public class CachedChat
{
    [PrimaryKey][Column("id")] public int Id { get; set; }
    [Column("name")] public string? Name { get; set; }
    [Column("type")] public int Type { get; set; }
    [Column("avatar")] public string? Avatar { get; set; }
    [Column("created_by_id")] public int CreatedById { get; set; }
    [Column("last_message_date")] public long? LastMessageDateTicks { get; set; }
    [Column("last_message_preview")] public string? LastMessagePreview { get; set; }
    [Column("last_message_sender_name")] public string? LastMessageSenderName { get; set; }
    [Column("last_message_sender_id")] public int? LastMessageSenderId { get; set; }
    [Column("last_message_is_system")] public bool LastMessageIsSystem { get; set; }
    [Column("last_message_is_poll")] public bool LastMessageIsPoll { get; set; }
    [Column("last_message_is_voice")] public bool LastMessageIsVoice { get; set; }
    [Column("cached_at")] public long CachedAtTicks { get; set; }

    [Column("show_history_for_new_members")]
    public bool ShowHistoryForNewMembers { get; set; } = true;

    [Column("contact_user_id")] public int? ContactUserId { get; set; }
    [Column("contact_is_online")] public bool ContactIsOnline { get; set; }
    [Column("contact_status_type")] public int ContactStatusType { get; set; }
    [Column("contact_status_expires_at")] public long? ContactStatusExpiresAtTicks { get; set; }

    [Ignore]
    public DateTime? LastMessageDate => LastMessageDateTicks.HasValue ? new DateTime(LastMessageDateTicks.Value, DateTimeKind.Utc) : null;

    [Ignore]
    public DateTime? ContactStatusExpiresAt => ContactStatusExpiresAtTicks.HasValue ? new DateTime(ContactStatusExpiresAtTicks.Value, DateTimeKind.Utc) : null;
}