using MessengerShared.Enum;

namespace MessengerShared.DTO.Chat;

public class UpdateChatDTO
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ChatType? ChatType { get; set; }
}