using Shared.Enum;

namespace API.Application.Features.Chat.Commands;

public sealed record UpdateChatCommand(
    int ChatId,
    int UserId,
    string? Name,
    ChatType? ChatType,
    bool? ShowHistoryForNewMembers
);
