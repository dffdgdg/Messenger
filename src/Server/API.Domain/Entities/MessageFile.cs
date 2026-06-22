namespace API.Domain.Entities;

public class MessageFile
{
    public int Id { get; set; }
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public int MessageId { get; set; }
    public string? Path { get; set; }
    public virtual UserMessage Message { get; set; } = null!;
}