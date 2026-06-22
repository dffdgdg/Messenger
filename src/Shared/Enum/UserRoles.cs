namespace Shared.Enum;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    User = 0,
    Head = 1,
    Admin = 2
}