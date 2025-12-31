namespace MessengerAPI.Model;

public class MessageStatus
{
    public int Id { get; set; }

    public int MessageId { get; set; }

    public int UserId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime UpdatedAt { get; set; }

    public virtual Message Message { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
