using System.ComponentModel.DataAnnotations.Schema;

namespace API.Data;

public partial class UserSetting
{
    public Theme? Theme { get; set; }
}
public partial class ChatMember
{
    public ChatRole Role { get; set; }
}
public partial class Chat
{
    public ChatType Type { get; set; }
}

public partial class User
{
    [NotMapped]
    public string? DisplayName
    {
        get
        {
            var parts = new[] { Surname, Name, Midname }.Where(s => !string.IsNullOrWhiteSpace(s));
            return parts.Any() ? string.Join(" ", parts) : null;
        }
    }

    public string GetDisplayName() => DisplayName ?? Username ?? "Без имени";
}