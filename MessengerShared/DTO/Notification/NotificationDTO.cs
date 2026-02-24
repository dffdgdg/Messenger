namespace MessengerShared.Dto.Notification;

public class NotificationDto
{
    public string Type { get; set; } = "message";
    public int ChatId { get; set; }
    public string? ChatName { get; set; }
    public string? ChatAvatar { get; set; }
    public int? MessageId { get; set; }
    public int SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderAvatar { get; set; }
    public string? Preview { get; set; }
    public DateTime CreatedAt { get; set; }
}