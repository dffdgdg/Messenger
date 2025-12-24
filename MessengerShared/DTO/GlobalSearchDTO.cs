using MessengerShared.Enum;

namespace MessengerShared.DTO;

public class GlobalSearchResponseDTO
{
    public List<ChatDTO> Chats { get; set; } = [];
    public List<GlobalSearchMessageDTO> Messages { get; set; } = [];
    public int TotalChatsCount { get; set; }
    public int TotalMessagesCount { get; set; }
    public int CurrentPage { get; set; }
    public bool HasMoreMessages { get; set; }
}

/// <summary>
/// Сообщение в результатах глобального поиска (с информацией о чате)
/// </summary>
public class GlobalSearchMessageDTO
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string? ChatName { get; set; }
    public string? ChatAvatar { get; set; }
    public ChatType ChatType { get; set; }
    public int SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? HighlightedContent { get; set; }
    public bool HasFiles { get; set; }
}