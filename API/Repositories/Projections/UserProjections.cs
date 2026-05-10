namespace API.Repositories.Projections;

public sealed record UserWithSettingsProjection(
    int Id,
    string Username,
    string? Surname,
    string? Name,
    string? Midname,
    string? Avatar,
    int? DepartmentId,
    string? DepartmentName,
    bool IsBanned,
    DateTime? LastOnline,
    DateTime? CreatedAt,
    Theme? Theme,
    bool NotificationsEnabled,
    UserStatusType StatusType,
    DateTime? StatusExpiresAt
);