namespace MessengerShared.DTO.Search;

public class GlobalSearchResponseDTO
{
    public List<ChatDTO> Chats { get; set; } = [];
    public List<GlobalSearchMessageDTO> Messages { get; set; } = [];
    public int TotalChatsCount { get; set; }
    public int TotalMessagesCount { get; set; }
    public int CurrentPage { get; set; }
    public bool HasMoreMessages { get; set; }
}