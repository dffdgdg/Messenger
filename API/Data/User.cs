using System.ComponentModel.DataAnnotations.Schema;

namespace API.Data;

public partial class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string? Name { get; set; }

    // ❌ Удалить:
    // public string PasswordHash { get; set; } = null!;

    // ✅ Новое свойство — owned-тип
    public UserPassword Password { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }
    public DateTime? LastOnline { get; set; }
    public int? DepartmentId { get; set; }
    public string? Avatar { get; set; }
    public string? Midname { get; set; }
    public string? Surname { get; set; }
    public bool IsBanned { get; set; }
    public UserStatusType StatusType { get; set; } = UserStatusType.Online;
    public DateTime? StatusExpiresAt { get; set; }

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = [];
    public virtual ICollection<Chat> Chats { get; set; } = [];
    public virtual Department? Department { get; set; }
    public virtual ICollection<Department> Departments { get; set; } = [];
    public virtual ICollection<Message> Messages { get; set; } = [];
    public virtual ICollection<PollVote> PollVotes { get; set; } = [];
    public virtual UserSetting? UserSetting { get; set; }
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}