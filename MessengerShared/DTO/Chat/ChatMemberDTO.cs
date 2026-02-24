using MessengerShared.Enum;

namespace MessengerShared.Dto.Chat;

public class ChatMemberDto
{
    public int ChatId { get; set; }
    public int UserId { get; set; }
    public ChatRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool NotificationsEnabled { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Avatar { get; set; }
}