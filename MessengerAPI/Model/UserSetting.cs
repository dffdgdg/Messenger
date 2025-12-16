using System;
using System.Collections.Generic;

namespace MessengerAPI.Model;

public partial class UserSetting
{
    public int UserId { get; set; }

    public bool? NotificationsEnabled { get; set; }

    public bool? CanBeFoundInSearch { get; set; }

    public virtual User User { get; set; } = null!;
}
