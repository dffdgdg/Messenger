namespace MessengerAPI.Model;

public partial class MessageFile
{
    public int Id { get; set; }

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public int MessageId { get; set; }

    public string? Path { get; set; }

    public virtual Message Message { get; set; } = null!;
}
