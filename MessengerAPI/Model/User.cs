using System;
using System.Collections.Generic;

namespace MessengerAPI.Model;

public partial class User
{
    public int Id { get; set; }

    public string Username { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string PasswordHash { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public DateTime? LastOnline { get; set; }

    public int? Department { get; set; }

    public string? Avatar { get; set; }

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    public virtual ICollection<Chat> Chats { get; set; } = new List<Chat>();

    public virtual Department? DepartmentNavigation { get; set; }

    public virtual ICollection<Draft> Drafts { get; set; } = new List<Draft>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual ICollection<PollVote> PollVotes { get; set; } = new List<PollVote>();

    public virtual UserSetting? UserSetting { get; set; }
}
