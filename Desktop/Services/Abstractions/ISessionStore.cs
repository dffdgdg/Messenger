namespace Desktop.Services.Abstractions;

public interface ISessionStore
{
    int? UserId { get; }
    string? Token { get; }
    UserRole UserRole { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    bool IsHead { get; }
    bool IsUser { get; }

    void SetSession(string token, int userId, UserRole role);
    void UpdateRole(UserRole role);
    void UpdateTokens(string token);
    void ClearSession();

    bool HasRole(UserRole requiredRole);
    bool HasAnyRole(params UserRole[] roles);
    bool IsInRole(UserRole role);

    event Action? SessionChanged;
}