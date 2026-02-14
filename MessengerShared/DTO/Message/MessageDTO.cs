using MessengerShared.DTO.Poll;

namespace MessengerShared.DTO.Message;

public class MessageDTO
{
    public bool IsVoiceMessage { get; set; }
    public string? TranscriptionStatus { get; set; }
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderAvatarUrl { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public PollDTO? Poll { get; set; }
    public bool IsOwn { get; set; }
    public bool IsPrevSameSender { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public int? ReplyToMessageId { get; set; }
    public MessageReplyPreviewDTO? ReplyToMessage { get; set; }
    public int? ForwardedFromMessageId { get; set; }
    public MessageForwardInfoDTO? ForwardedFrom { get; set; }
    public MessageDTO? PreviousMessage { get; set; }
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

    public List<MessageFileDTO> Files { get; set; } = [];
}