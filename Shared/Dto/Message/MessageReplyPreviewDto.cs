namespace Shared.Dto.Message;

public class MessageReplyPreviewDto
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int? SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsVoiceMessage { get; set; }
    public bool HasPoll { get; set; }
    public int FilesCount { get; set; }
}