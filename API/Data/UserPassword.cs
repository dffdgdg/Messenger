namespace API.Data;

/// <summary>
/// Owned-тип для хранения пароля.
/// Не загружается автоматически — только через .Include(u => u.Password)
/// </summary>
public sealed class UserPassword
{
    public string Hash { get; set; } = null!;

    public void SetPassword(string plainPassword) => Hash = BCrypt.Net.BCrypt.HashPassword(plainPassword);

    public bool Verify(string plainPassword) => BCrypt.Net.BCrypt.Verify(plainPassword, Hash);
}