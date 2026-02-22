using SQLite;
using System;

namespace MessengerDesktop.Data.Entities;

[SQLite.Table("messages")]
public class CachedMessage
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    public int Id { get; set; }

    [SQLite.Indexed]
    [SQLite.Column("chat_id")]
    public int ChatId { get; set; }

    [SQLite.Indexed]
    [SQLite.Column("sender_id")]
    public int SenderId { get; set; }

    [SQLite.Column("content")]
    public string? Content { get; set; }

    [SQLite.Column("created_at")]
    public long CreatedAtTicks { get; set; }

    [SQLite.Column("edited_at")]
    public long? EditedAtTicks { get; set; }

    [SQLite.Column("is_deleted")]
    public bool IsDeleted { get; set; }

    [SQLite.Column("is_voice_message")]
    public bool IsVoiceMessage { get; set; }

    [SQLite.Column("transcription_status")]
    public string? TranscriptionStatus { get; set; }

    [SQLite.Column("reply_to_message_id")]
    public int? ReplyToMessageId { get; set; }

    [SQLite.Column("forwarded_from_message_id")]
    public int? ForwardedFromMessageId { get; set; }

    [SQLite.Column("is_own")]
    public bool IsOwn { get; set; }

    // ── Sender info ──
    [SQLite.Column("sender_name")]
    public string? SenderName { get; set; }

    [SQLite.Column("sender_avatar_url")]
    public string? SenderAvatarUrl { get; set; }

    // ── Reply preview ──
    [SQLite.Column("reply_sender_name")]
    public string? ReplySenderName { get; set; }

    [SQLite.Column("reply_content_preview")]
    public string? ReplyContentPreview { get; set; }

    [SQLite.Column("reply_is_deleted")]
    public bool ReplyIsDeleted { get; set; }

    [SQLite.Column("reply_sender_id")]
    public int? ReplySenderId { get; set; }

    [SQLite.Column("reply_chat_id")]
    public int? ReplyChatId { get; set; }

    // ── Forward info ──
    [SQLite.Column("forward_sender_name")]
    public string? ForwardSenderName { get; set; }

    [SQLite.Column("forward_original_sender_id")]
    public int? ForwardOriginalSenderId { get; set; }

    [SQLite.Column("forward_original_chat_id")]
    public int? ForwardOriginalChatId { get; set; }

    [SQLite.Column("forward_original_date")]
    public long? ForwardOriginalDateTicks { get; set; }

    // ── Poll — сериализован в JSON ──
    [SQLite.Column("poll_json")]
    public string? PollJson { get; set; }

    // ── Files metadata — JSON array ──
    [SQLite.Column("files_json")]
    public string? FilesJson { get; set; }

    // ── Версия записи (для conflict resolution) ──
    [SQLite.Column("cached_at")]
    public long CachedAtTicks { get; set; }

    // ── Helpers (не хранятся, только для маппинга) ──
    [Ignore] public DateTime CreatedAt => new(CreatedAtTicks, DateTimeKind.Utc);
    [Ignore] public DateTime? EditedAt => EditedAtTicks.HasValue ? new DateTime(EditedAtTicks.Value, DateTimeKind.Utc) : null;
    [Ignore] public DateTime CachedAt => new(CachedAtTicks, DateTimeKind.Utc);
}