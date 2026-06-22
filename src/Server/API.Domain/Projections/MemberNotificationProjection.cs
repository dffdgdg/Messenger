namespace API.Domain.Projections;

public sealed record MemberNotificationProjection(
    int UserId,
    string? Username,
    bool ChatNotificationsEnabled,
    bool GlobalNotificationsEnabled
);