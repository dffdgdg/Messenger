namespace API.Application.Features.Chat.Commands;

public sealed record DeleteChatCommand(int ChatId, int UserId);
