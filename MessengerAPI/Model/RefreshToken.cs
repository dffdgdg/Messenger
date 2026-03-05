namespace MessengerAPI.Model;

public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>
    /// SHA-256 хеш токена. Сам токен не хранится.
    /// </summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>
    /// Jti (JWT ID) access-токена, с которым был выдан этот refresh token.
    /// При ротации позволяет привязать пару.
    /// </summary>
    public string JwtId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Null = активен. При ротации записывается время использования.
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// Null = не отозван. При компрометации вся семья ротации отзывается.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Если токен был использован для ротации — ссылка на новый токен.
    /// Позволяет обнаружить повторное использование (replay attack).
    /// </summary>
    public int? ReplacedByTokenId { get; set; }

    /// <summary>
    /// Семья ротации. Все токены одной цепочки имеют одинаковый FamilyId.
    /// При обнаружении повторного использования вся семья отзывается.
    /// </summary>
    public string FamilyId { get; set; } = null!;

    public bool IsActive => UsedAt == null && RevokedAt == null && ExpiresAt > DateTime.UtcNow;

    // Navigation
    public virtual User User { get; set; } = null!;
    public virtual RefreshToken? ReplacedByToken { get; set; }
}