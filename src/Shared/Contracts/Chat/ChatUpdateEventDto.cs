using Shared.Enum;
using System.Text.Json.Serialization;

namespace Shared.Contracts.Chat;

public class ChatUpdateEventDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ChatType Type { get; set; }
    public int CreatedById { get; set; }
    public string? Avatar { get; set; }
    public bool ShowHistoryForNewMembers { get; set; }

    /// <summary>
    /// Заполняется только при адресной отправке конкретному пользователю
    /// после смены его роли. Null = роль не изменилась.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatRole? CurrentUserRole { get; set; }
}