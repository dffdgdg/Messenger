using Shared.Dto.Poll;
using Shared.Enum;

namespace Shared.Dto.Message;

public class MessageDto
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int? SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderAvatarUrl { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public PollDto? Poll { get; set; }
    public bool IsOwn { get; set; }
    public bool IsPrevSameSender { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsPinned { get; set; }
    public bool IsSystemMessage { get; set; }
    public DateTime? PinnedAt { get; set; }
    public int? PinnedByUserId { get; set; }
    public int? ReplyToMessageId { get; set; }
    public MessageReplyPreviewDto? ReplyToMessage { get; set; }
    public int? ForwardedFromMessageId { get; set; }
    public MessageForwardInfoDto? ForwardedFrom { get; set; }
    public MessageDto? PreviousMessage { get; set; }

    public bool ShowSenderName
    {
        get
        {
            if (PreviousMessage == null)
                return true;
            if (SenderId != PreviousMessage.SenderId)
                return true;
            var timeDiff = CreatedAt - PreviousMessage.CreatedAt;
            return timeDiff.TotalMinutes > 5;
        }
    }

    public SystemEventType? SystemEventType { get; set; }
    public int? TargetUserId { get; set; }
    public string? TargetUserName { get; set; }

    public bool IsVoiceMessage { get; set; }
    public double? VoiceDurationSeconds { get; set; }
    public string? VoiceWaveform { get; set; }
    public string? VoiceFileUrl { get; set; }
    public long? VoiceFileSize { get; set; }
    public List<MessageFileDto> Files { get; set; } = [];
}