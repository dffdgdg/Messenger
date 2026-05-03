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

    public static string FormatDisplayNameStatic(string? surname, string? name, string? midname)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(surname)) parts.Add(surname);
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
        if (!string.IsNullOrWhiteSpace(midname)) parts.Add(midname);
        return parts.Count > 0 ? string.Join(" ", parts) : "Без имени";
    }
}
