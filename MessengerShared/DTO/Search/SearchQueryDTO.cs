namespace MessengerShared.Dto.Search;

public class SearchMessagesQueryDto
{
    public string Query { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? SenderId { get; set; }
    public bool? HasFiles { get; set; }
    public bool? HasVoice { get; set; }
    public bool? HasPoll { get; set; }
    public bool? OnlyText { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool OldestFirst { get; set; }
}

public sealed class GlobalSearchQueryDto : SearchMessagesQueryDto
{
    public int? FilterChatId { get; set; }
}