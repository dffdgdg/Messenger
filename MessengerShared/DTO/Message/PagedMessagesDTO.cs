namespace MessengerShared.Dto.Message;

public class PagedMessagesDto
{
    public List<MessageDto> Messages { get; set; } = [];
    public int TotalCount { get; set; }
    public bool HasMoreMessages { get; set; }
    public bool HasNewerMessages { get; set; }
    public int CurrentPage { get; set; }
}