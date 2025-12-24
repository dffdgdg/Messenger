using CommunityToolkit.Mvvm.ComponentModel;
using MessengerShared.Enum;
using System;
using System.Collections.Generic;

namespace MessengerDesktop.Services.Auth;

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
    private UserRole _userRole = UserRole.User;

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token) && UserId.HasValue;
    public bool IsAdmin => UserRole == UserRole.Admin;
    public bool IsHead => UserRole == UserRole.Head;
    public bool IsUser => UserRole == UserRole.User;

    public event Action? SessionChanged;

    public void SetSession(string token, int userId, UserRole role)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("Token cannot be null or empty", nameof(token));

        if (userId <= 0)
            throw new ArgumentException("UserId must be positive", nameof(userId));

        Token = token;
        UserId = userId;
        UserRole = role;

        NotifySessionPropertiesChanged();
        SessionChanged?.Invoke();
    }

    public void ClearSession()
    {
        Token = null;
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
        if (!IsAuthenticated)
            return false;

        if (!RoleHierarchy.TryGetValue(UserRole, out var currentLevel))
            return false;

        if (!RoleHierarchy.TryGetValue(requiredRole, out var requiredLevel))
            return false;

        return currentLevel >= requiredLevel;
    }

    public bool HasAnyRole(params UserRole[] roles)
    {
        if (roles == null || roles.Length == 0)
            return false;

        if (!IsAuthenticated)
            return false;

        foreach (var role in roles)
        {
            if (IsInRole(role))
                return true;
        }

        return false;
    }

    public bool IsInRole(UserRole role) => IsAuthenticated && UserRole == role;
}