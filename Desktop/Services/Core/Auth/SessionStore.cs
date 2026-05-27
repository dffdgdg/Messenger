namespace Desktop.Services.Auth;

public partial class SessionStore : ObservableObject, ISessionStore
{
    [ObservableProperty] public partial int? UserId { get; set; }
    [ObservableProperty] public partial string? Token { get; set; }

    [ObservableProperty] public partial UserRole UserRole { get; set; } = UserRole.User;

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token) && UserId.HasValue;
    public bool IsAdmin => UserRole.HasFlag(UserRole.Admin);
    public bool IsHead => UserRole.HasFlag(UserRole.Head);
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

    public void UpdateTokens(string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("Token cannot be null or empty", nameof(token));

        Token = token;
        SessionChanged?.Invoke();
    }

    public void UpdateRole(UserRole role)
    {
        if (!IsAuthenticated || UserRole == role) return;
        UserRole = role;
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(IsHead));
        OnPropertyChanged(nameof(IsUser));
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

    public bool HasRole(UserRole requiredRole) => UserRole.HasFlag(requiredRole);

    public bool HasAnyRole(params UserRole[] roles)
    {
        if (roles.Length == 0 || !IsAuthenticated) return false;
        return roles.Any(r => UserRole.HasFlag(r));
    }

    public bool IsInRole(UserRole role) => IsAuthenticated && UserRole.HasFlag(role);
}