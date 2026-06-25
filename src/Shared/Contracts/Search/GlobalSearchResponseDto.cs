using Shared.Contracts.Chat;

namespace Shared.Contracts.Search;

public class GlobalSearchResponseDto
{
    public List<ChatDto> Chats { get; set; } = [];
    public List<GlobalSearchMessageDto> Messages { get; set; } = [];
    public int TotalChatsCount { get; set; }
    public int TotalMessagesCount { get; set; }
    public int CurrentPage { get; set; }
    public bool HasMoreMessages { get; set; }
}