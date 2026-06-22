using Shared.Enum;

namespace API.Domain.Projections;

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