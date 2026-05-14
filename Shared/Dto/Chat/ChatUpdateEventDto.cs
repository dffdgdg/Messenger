using Shared.Enum;

namespace Shared.Dto.Chat;

public class ChatUpdateEventDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ChatType Type { get; set; }
    public int CreatedById { get; set; }
    public string? Avatar { get; set; }
    public bool ShowHistoryForNewMembers { get; set; }
}