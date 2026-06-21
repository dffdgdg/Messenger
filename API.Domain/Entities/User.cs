using Shared.Enum;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Domain.Entities;

public partial class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string? Name { get; set; }
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
    [NotMapped]
    public string? DisplayName
    {
        get
        {
            var parts = new[] { Surname, Name, Midname }.Where(s => !string.IsNullOrWhiteSpace(s));
            return parts.Any() ? string.Join(" ", parts) : null;
        }
    }

    public string GetDisplayName() => DisplayName ?? Username ?? "Без имени";
    public virtual ICollection<ChatMember> ChatMembers { get; set; } = [];
    public virtual ICollection<Chat> Chats { get; set; } = [];
    public virtual Department? Department { get; set; }
    public virtual ICollection<Department> Departments { get; set; } = [];
    public virtual ICollection<UserMessage> SentMessages { get; set; } = [];
    public virtual ICollection<PollVote> PollVotes { get; set; } = [];
    public virtual UserSetting? UserSetting { get; set; }
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}