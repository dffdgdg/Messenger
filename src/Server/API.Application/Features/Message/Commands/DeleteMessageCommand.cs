namespace API.Application.Features.Message.Commands;

public sealed record DeleteMessageCommand(int MessageId, int UserId);
