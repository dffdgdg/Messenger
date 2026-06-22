using Shared.Enum;

namespace API.Domain.Entities;

public abstract class Message
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool? IsDeleted { get; set; }
    public DateTime? PinnedAt { get; set; }
    public int? PinnedByUserId { get; set; }

    public virtual Chat Chat { get; set; } = null!;
    public virtual User? PinnedByUser { get; set; }
    public virtual ICollection<ChatMember> ChatMembers { get; set; } = [];
}

public class UserMessage : Message
{
    public int? SenderId { get; set; }
    public string? Content { get; set; }
    public DateTime? EditedAt { get; set; }
    public int? ReplyToMessageId { get; set; }
    public int? ForwardedFromMessageId { get; set; }

    public virtual VoiceMessage? VoiceMessage { get; set; }
    public virtual User? Sender { get; set; }
    public virtual UserMessage? ReplyToMessage { get; set; }
    public virtual UserMessage? ForwardedFromMessage { get; set; }
    public virtual ICollection<MessageFile> MessageFiles { get; set; } = [];
    public virtual Poll? Poll { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsVoiceMessage => VoiceMessage != null;
    public virtual ICollection<UserMessage> InverseReplyToMessage { get; set; } = [];
    public virtual ICollection<UserMessage> InverseForwardedFromMessage { get; set; } = [];
}

public class SystemMessage : Message
{
    public int? InitiatorId { get; set; }
    public int? TargetUserId { get; set; }
    public SystemEventType SystemEventType { get; set; }
    public string? Content { get; set; }

    public virtual User? Initiator { get; set; }
    public virtual User? TargetUser { get; set; }
}