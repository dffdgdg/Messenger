using Shared.Enum;

namespace API.Domain.Projections;

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