namespace MessengerShared.Dto.Call;

public class CallChatMessageDto
{
    public string CallId { get; set; } = string.Empty;
    public int SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderAvatar { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}