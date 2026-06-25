using Shared.Contracts.ReadReceipt;

namespace API.Application.Features.ReadReceipt.Commands;

public sealed record MarkAsReadCommand(int UserId, MarkAsReadDto Request);
