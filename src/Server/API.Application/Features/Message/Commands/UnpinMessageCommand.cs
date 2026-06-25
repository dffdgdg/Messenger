namespace API.Application.Features.Message.Commands;

public sealed record UnpinMessageCommand(int MessageId, int UserId);
