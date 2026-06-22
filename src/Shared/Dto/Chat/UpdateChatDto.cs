using Shared.Enum;

namespace Shared.Dto.Chat;

public class UpdateChatDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ChatType? ChatType { get; set; }
    public bool? ShowHistoryForNewMembers { get; set; }
}