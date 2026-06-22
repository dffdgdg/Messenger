using Shared.Enum;

namespace API.Domain.Entities;

public partial class UserSetting
{
    public int UserId { get; set; }
    public bool NotificationsEnabled { get; set; }
    public virtual User User { get; set; } = null!;
    public Theme? Theme { get; set; }
}