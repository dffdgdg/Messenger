using System;
using System.Collections.Generic;

namespace MessengerAPI.Model;

public partial class ChatMember
{
    public int ChatId { get; set; }

    public int UserId { get; set; }

    public DateTime JoinedAt { get; set; }

    public virtual Chat Chat { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
