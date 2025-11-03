namespace MessengerAPI.Model;

public partial class Draft
{
    public int UserId { get; set; }

    public int ChatId { get; set; }

    public string? Content { get; set; }

    public DateTime LastUpdated { get; set; }

    public virtual Chat Chat { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
