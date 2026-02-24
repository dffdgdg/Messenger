namespace MessengerShared.Dto.Message;

public class MessageForwardInfoDto
{
    public int OriginalMessageId { get; set; }
    public int OriginalChatId { get; set; }
    public int OriginalSenderId { get; set; }
    public string? OriginalSenderName { get; set; }
    public DateTime OriginalCreatedAt { get; set; }
}