using MessengerShared.Enum;

namespace MessengerShared.DTO.Chat;

public class ChatDTO
{
    public int Id { get; set; }
    public string? Name { get; set; } = string.Empty;
    public ChatType Type { get; set; } = ChatType.Chat;
    public int CreatedById { get; set; }
    public DateTime? LastMessageDate { get; set; }
    public string? Avatar { get; set; } = string.Empty;
    public string? LastMessagePreview { get; set; }
    public string? LastMessageSenderName { get; set; }
    public int UnreadCount { get; set; }
}