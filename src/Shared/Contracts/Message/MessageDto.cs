using Shared.Contracts.Poll;
using Shared.Enum;

namespace Shared.Contracts.Message;

public class MessageDto
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int? SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderAvatarUrl { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOwn { get; set; }
    public bool IsPrevSameSender { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsPinned { get; set; }
    public bool IsSystemMessage { get; set; }
    public bool IsVoiceMessage { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTime? EditedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTime? PinnedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? PinnedByUserId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ReplyToMessageId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MessageReplyPreViewDto? ReplyToMessage { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ForwardedFromMessageId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MessageForwardInfoDto? ForwardedFrom { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SystemEventType? SystemEventType { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? TargetUserId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TargetUserName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? VoiceDurationSeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? VoiceWaveform { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? VoiceFileUrl { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? VoiceFileSize { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PollDto? Poll { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<MessageFileDto>? Files { get; set; }
}