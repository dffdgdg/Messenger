namespace API.Application.Services.Abstractions;

/// <summary>
/// Предоставляет хабу доступ к пользовательским данным
/// без прямого обращения к DbContext.
/// </summary>
public interface IUserInfoService
{
    Task<UserDisplayInfo?> GetDisplayInfoAsync(int userId, CancellationToken ct = default);
    Task<string> GetChatNameAsync(int chatId, CancellationToken ct = default);
    Task UpdateLastOnlineAsync(int userId, DateTime lastOnline, CancellationToken ct = default);
}

public sealed record UserDisplayInfo(string DisplayName, string? Avatar)
{
    public static UserDisplayInfo Unknown(int userId) => new($"User {userId}", null);
}
