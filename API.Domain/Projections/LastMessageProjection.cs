using Shared.Enum;

namespace API.Domain.Projections;

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