using Shared.Contracts.Message;

namespace API.Application.Features.Message.Commands;

public sealed record CreateMessageCommand(int SenderId, CreateMessageRequest Request);
