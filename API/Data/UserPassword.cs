namespace API.Data;

/// <summary>
/// Owned-тип для хранения пароля.
/// Не загружается автоматически — только через .Include(u => u.Password)
/// </summary>
public sealed class UserPassword
{
    public string Hash { get; set; } = null!;
    public static UserPassword Create(string password, int workFactor = 12) => new() { Hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor) };
    public void SetPassword(string plainPassword) => Hash = BCrypt.Net.BCrypt.HashPassword(plainPassword);

    public bool Verify(string plainPassword) => BCrypt.Net.BCrypt.Verify(plainPassword, Hash);
}