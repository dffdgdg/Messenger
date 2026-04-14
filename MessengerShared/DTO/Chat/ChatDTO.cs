using MessengerShared.Enum;

namespace MessengerShared.Dto.Chat;

public class ChatDto
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

    public int? LastMessageSenderId { get; set; }
    public bool LastMessageIsSystem { get; set; }
    public bool LastMessageIsPoll { get; set; }
    public bool LastMessageIsVoice { get; set; }
    public bool LastMessageHasFilesOnly { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HideSenderPrefix { get; set; }
}