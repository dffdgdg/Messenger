using Shared.Enum;

namespace API.Application.Features.Status.Commands;

public sealed record SetStatusCommand(int UserId, UserStatusType StatusType, TimeSpan? Duration);
