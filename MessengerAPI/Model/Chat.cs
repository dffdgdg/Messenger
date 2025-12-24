using System;
using System.Collections.Generic;

namespace MessengerAPI.Model;

public partial class Chat
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public DateTime? LastMessageTime { get; set; }

    public string? Avatar { get; set; }

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    public virtual User? CreatedBy { get; set; }

    public virtual Department? Department { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
