using Shared.Contracts.Message;

namespace API.Application.Features.Message.Commands;

public sealed record UpdateMessageCommand(int MessageId, int UserId, UpdateMessageDto Dto);
