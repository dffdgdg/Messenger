using System;
using System.Collections.Generic;

namespace MessengerAPI.Model;

public partial class ChatMember
{
    public int ChatId { get; set; }

    public int UserId { get; set; }

    public DateTime JoinedAt { get; set; }

    public bool NotificationsEnabled { get; set; }

    public int? LastReadMessageId { get; set; }

    public DateTime? LastReadAt { get; set; }

    public virtual Chat Chat { get; set; } = null!;

    public virtual Message? LastReadMessage { get; set; }

    public virtual User User { get; set; } = null!;
}
