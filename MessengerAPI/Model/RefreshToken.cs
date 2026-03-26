namespace MessengerAPI.Model;

public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public string JwtId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public int? ReplacedByTokenId { get; set; }
    public string FamilyId { get; set; } = null!;
    public bool IsActive => UsedAt == null && RevokedAt == null && ExpiresAt > DateTime.UtcNow;
    public virtual User User { get; set; } = null!;
    public virtual RefreshToken? ReplacedByToken { get; set; }
}