namespace MessengerAPI.Model;

public partial class User
{
    public int Id { get; set; }

    public string Username { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string PasswordHash { get; set; } = null!;

    public string PasswordSalt { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public DateTime? LastOnline { get; set; }

    public int? Department { get; set; }

    public string? Avatar { get; set; }

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = [];

    public virtual ICollection<Chat> Chats { get; set; } = [];

    public virtual Department? DepartmentNavigation { get; set; }

    public virtual ICollection<Draft> Drafts { get; set; } = [];

    public virtual ICollection<Message> Messages { get; set; } = [];

    public virtual ICollection<PollVote> PollVotes { get; set; } = [];

    public virtual ICollection<Poll> Polls { get; set; } = [];

    public virtual UserSetting? UserSetting { get; set; }
}
