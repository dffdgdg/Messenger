namespace MessengerAPI.Model;

public class Message
{
    public int Id { get; set; }

    public int ChatId { get; set; }

    public int SenderId { get; set; }

    public string? Content { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool? IsDeleted { get; set; }

    public int? ReplyToMessageId { get; set; }

    public int? ForwardedFromMessageId { get; set; }

    public virtual Chat Chat { get; set; } = null!;

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = [];

    public virtual ICollection<MessageFile> MessageFiles { get; set; } = [];

    public virtual ICollection<Message> InverseReplyToMessage { get; set; } = [];

    public virtual ICollection<Message> InverseForwardedFromMessage { get; set; } = [];

    public virtual Message? ReplyToMessage { get; set; }

    public virtual Message? ForwardedFromMessage { get; set; }

    public virtual ICollection<Poll> Polls { get; set; } = [];

    public virtual User Sender { get; set; } = null!;
}