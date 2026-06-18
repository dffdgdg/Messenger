using SQLite;

namespace Core.Data.Models.Cache;

[Table("messages")]
public class CachedMessage
{
    [PrimaryKey][Column("id")] public int Id { get; set; }
    [Indexed][Column("chat_id")] public int ChatId { get; set; }
    [Indexed][Column("sender_id")] public int? SenderId { get; set; }
    [Column("content")] public string? Content { get; set; }
    [Column("created_at")] public long CreatedAtTicks { get; set; }
    [Column("edited_at")] public long? EditedAtTicks { get; set; }
    [Column("is_deleted")] public bool IsDeleted { get; set; }
    [Column("reply_to_message_id")] public int? ReplyToMessageId { get; set; }
    [Column("forwarded_from_message_id")] public int? ForwardedFromMessageId { get; set; }
    [Column("is_own")] public bool IsOwn { get; set; }
    [Column("is_voice_message")] public bool IsVoiceMessage { get; set; }
    [Column("voice_duration_seconds")] public double? VoiceDurationSeconds { get; set; }
    [Column("voice_waveform")] public string? VoiceWaveform { get; set; }
    [Column("voice_file_url")] public string? VoiceFileUrl { get; set; }
    [Column("voice_file_name")] public string? VoiceFileName { get; set; }
    [Column("voice_content_type")] public string? VoiceContentType { get; set; }
    [Column("voice_file_size")] public long? VoiceFileSize { get; set; }
    [Column("sender_name")] public string? SenderName { get; set; }
    [Column("sender_avatar_url")] public string? SenderAvatarUrl { get; set; }
    [Column("reply_sender_name")] public string? ReplySenderName { get; set; }
    [Column("reply_content_preview")] public string? ReplyContentPreview { get; set; }
    [Column("reply_is_deleted")] public bool ReplyIsDeleted { get; set; }
    [Column("reply_sender_id")] public int? ReplySenderId { get; set; }
    [Column("reply_chat_id")] public int? ReplyChatId { get; set; }
    [Column("forward_sender_name")] public string? ForwardSenderName { get; set; }
    [Column("forward_original_sender_id")] public int? ForwardOriginalSenderId { get; set; }
    [Column("forward_original_chat_id")] public int? ForwardOriginalChatId { get; set; }
    [Column("forward_original_date")] public long? ForwardOriginalDateTicks { get; set; }
    [Column("poll_json")] public string? PollJson { get; set; }
    [Column("files_json")] public string? FilesJson { get; set; }
    [Column("cached_at")] public long CachedAtTicks { get; set; }
    [Column("is_pinned")] public bool IsPinned { get; set; }
    [Column("reply_is_voice")] public bool ReplyIsVoice { get; set; }
    [Column("reply_has_poll")] public bool ReplyHasPoll { get; set; }
    [Column("reply_files_count")] public int ReplyFilesCount { get; set; }
    public bool IsSystemMessage { get; set; }
    public int? SystemEventTypeInt { get; set; }
    public int? TargetUserId { get; set; }
    public string? TargetUserName { get; set; }
    [Ignore] public DateTime CreatedAt => new(CreatedAtTicks, DateTimeKind.Utc);
    [Ignore] public DateTime? EditedAt => EditedAtTicks.HasValue ? new DateTime(EditedAtTicks.Value, DateTimeKind.Utc) : null;
    [Ignore] public DateTime CachedAt => new(CachedAtTicks, DateTimeKind.Utc);
}