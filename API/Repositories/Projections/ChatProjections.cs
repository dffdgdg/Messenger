namespace API.Repositories.Projections;

public sealed record LastMessageProjection(
    int Id,
    int ChatId,
    DateTime CreatedAt,
    bool IsSystemMessage,
    int? SenderId,
    string? Content,
    SystemEventType? SystemEventType,
    int? TargetUserId,
    string? SenderName,
    string? TargetUserName,
    bool IsVoiceMessage,
    bool HasPoll,
    bool HasFiles
);

public sealed record DialogPartnerProjection(
    int ChatId,
    int UserId,
    string? Surname,
    string? Name,
    string? Midname,
    string? Avatar,
    UserStatusType StatusType,
    DateTime? StatusExpiresAt
);

public sealed record ChatMemberProjection(
    int UserId,
    ChatRole Role,
    string Username,
    string? Surname,
    string? Name,
    string? Midname,
    string? Avatar,
    DateTime? LastOnline,
    UserStatusType StatusType,
    DateTime? StatusExpiresAt
);

public sealed record MemberNotificationProjection(
    int UserId,
    string? Username,
    bool ChatNotificationsEnabled,
    bool GlobalNotificationsEnabled
);