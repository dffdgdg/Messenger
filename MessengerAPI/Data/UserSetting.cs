namespace MessengerAPI.Data;

public partial class UserSetting
{
    public int UserId { get; set; }
    public bool NotificationsEnabled { get; set; }
    public virtual User User { get; set; } = null!;
}