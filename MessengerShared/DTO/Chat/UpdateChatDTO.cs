using MessengerShared.Enum;

namespace MessengerShared.Dto.Chat;

public class UpdateChatDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ChatType? ChatType { get; set; }
}