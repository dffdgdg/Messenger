using System;
using System.Collections.Generic;
using System.Linq;

namespace MessengerDesktop.Services.Auth;

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

    void SetSession(
        string token, string refreshToken,
        int userId, UserRole role);
    void UpdateTokens(string token, string refreshToken);
    void ClearSession();

    bool HasRole(UserRole requiredRole);
    bool HasAnyRole(params UserRole[] roles);
    bool IsInRole(UserRole role);

    event Action? SessionChanged;
}

public partial class SessionStore : ObservableObject, ISessionStore
{
    private static readonly Dictionary<UserRole, int> RoleHierarchy = new()
    {
        [UserRole.User] = 0,
        [UserRole.Head] = 1,
        [UserRole.Admin] = 2
    };

    [ObservableProperty]
    private int? _userId;

    [ObservableProperty]
    private string? _token;

    [ObservableProperty]
    private string? _refreshToken;

    [ObservableProperty]
    private UserRole _userRole = UserRole.User;

    public bool IsAuthenticated
        => !string.IsNullOrEmpty(Token) && UserId.HasValue;
    public bool IsAdmin => UserRole == UserRole.Admin;
    public bool IsHead => UserRole == UserRole.Head;
    public bool IsUser => UserRole == UserRole.User;

    public event Action? SessionChanged;

    public void SetSession(string token, string refreshToken, int userId, UserRole role)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("Token cannot be null or empty", nameof(token));
        if (string.IsNullOrEmpty(refreshToken))
            throw new ArgumentException("RefreshToken cannot be null or empty", nameof(refreshToken));
        if (userId <= 0)
            throw new ArgumentException("UserId must be positive", nameof(userId));

        Token = token;
        RefreshToken = refreshToken;
        UserId = userId;
        UserRole = role;

        NotifySessionPropertiesChanged();
        SessionChanged?.Invoke();
    }

    public void UpdateTokens(string token, string refreshToken)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("Token cannot be null or empty", nameof(token));
        if (string.IsNullOrEmpty(refreshToken))
            throw new ArgumentException("RefreshToken cannot be null or empty", nameof(refreshToken));

        Token = token;
        RefreshToken = refreshToken;

        SessionChanged?.Invoke();
    }

    public void ClearSession()
    {
        Token = null;
        RefreshToken = null;
        UserId = null;
        UserRole = UserRole.User;

        NotifySessionPropertiesChanged();
        SessionChanged?.Invoke();
    }

    private void NotifySessionPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(IsHead));
        OnPropertyChanged(nameof(IsUser));
    }

    public bool HasRole(UserRole requiredRole)
    {
        if (!IsAuthenticated) return false;
        if (!RoleHierarchy.TryGetValue(UserRole, out var currentLevel))
            return false;
        if (!RoleHierarchy.TryGetValue(requiredRole, out var requiredLevel))
            return false;
        return currentLevel >= requiredLevel;
    }

    public bool HasAnyRole(params UserRole[] roles)
    {
        if (roles.Length == 0 || !IsAuthenticated) return false;
        return roles.Any(IsInRole);
    }

    public bool IsInRole(UserRole role) => IsAuthenticated && UserRole == role;
}