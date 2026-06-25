using Shared.Enum;

namespace API.Application.Features.Chat.Commands;

public sealed record CreateChatCommand(
    int CreatorId,
    string? Name,
    ChatType Type,
    bool ShowHistoryForNewMembers
);
