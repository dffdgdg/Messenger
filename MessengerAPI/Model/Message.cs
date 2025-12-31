namespace MessengerAPI.Model;

public partial class Message
{
    public int Id { get; set; }

    public int ChatId { get; set; }

    public int SenderId { get; set; }

    public string? Content { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool? IsDeleted { get; set; }

    public virtual Chat Chat { get; set; } = null!;

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    public virtual ICollection<MessageFile> MessageFiles { get; set; } = new List<MessageFile>();

    public virtual ICollection<Poll> Polls { get; set; } = new List<Poll>();

    public virtual User Sender { get; set; } = null!;
}
