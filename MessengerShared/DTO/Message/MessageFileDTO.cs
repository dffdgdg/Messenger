namespace MessengerShared.DTO.Message;

public class MessageFileDTO
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string PreviewType { get; set; } = "file";
    public long FileSize { get; set; }
}