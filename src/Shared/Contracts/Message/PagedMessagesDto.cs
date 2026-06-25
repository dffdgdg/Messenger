namespace Shared.Contracts.Message;

public class PagedMessagesDto
{
    public List<MessageDto> Messages { get; set; } = [];
    public bool HasMoreMessages { get; set; }
    public bool HasNewerMessages { get; set; }
}