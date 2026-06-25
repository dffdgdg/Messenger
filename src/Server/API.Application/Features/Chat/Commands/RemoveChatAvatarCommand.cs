namespace API.Application.Features.Chat.Commands;

public sealed record RemoveChatAvatarCommand(int ChatId, int UserId);
