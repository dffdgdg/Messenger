namespace Desktop.Services.Abstractions;

public interface ISessionStore
{
    int? UserId { get; }
    string? Token { get; }
    string? RefreshToken { get; }
    UserRole UserRole { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    bool IsHead { get; }
    bool IsUser { get; }

    void SetSession(string token, string refreshToken, int userId, UserRole role);
    void UpdateTokens(string token, string refreshToken);
    void ClearSession();

    bool HasRole(UserRole requiredRole);
    bool HasAnyRole(params UserRole[] roles);
    bool IsInRole(UserRole role);

    event Action? SessionChanged;
}