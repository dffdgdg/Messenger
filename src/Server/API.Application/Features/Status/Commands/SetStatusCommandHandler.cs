using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;

namespace API.Application.Features.Status.Commands;

public class SetStatusCommandHandler(IUserStatusService userStatusService)
    : ICommandHandler<SetStatusCommand>
{
    public virtual async Task<Result> HandleAsync(SetStatusCommand command, CancellationToken ct = default)
        => await userStatusService.SetStatusAsync(command.UserId, command.StatusType, command.Duration);
}

