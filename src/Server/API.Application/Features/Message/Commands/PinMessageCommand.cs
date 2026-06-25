namespace API.Application.Features.Message.Commands;

public sealed record PinMessageCommand(int MessageId, int UserId);
