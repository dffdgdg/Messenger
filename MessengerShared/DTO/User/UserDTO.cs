using MessengerShared.Enum;

namespace MessengerShared.Dto.User;

public class UserDto
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Name { get; set; }
    public string? Midname { get; set; }
    public string? Surname { get; set; }
    public string? Department { get; set; }
    public int? DepartmentId { get; set; }
    public string? Avatar { get; set; }
    public bool IsOnline { get; set; }
    public bool IsBanned { get; set; }
    public DateTime? LastOnline { get; set; }
    public Theme? Theme { get; set; }
    public bool? NotificationsEnabled { get; set; }
    public bool? SoundsEnabled { get; set; }
}