using Shared.Dto.Message;

namespace Shared.Dto.Search;

public class SearchMessagesResponseDto
{
    public List<MessageDto> Messages { get; set; } = [];
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; }
    public bool HasMoreMessages { get; set; }
}