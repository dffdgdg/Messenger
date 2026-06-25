using API.Application.Common;
using API.Application.Features.Status.Commands;
using API.Application.Features.Status.Queries;
using API.Domain.Common;
using Shared.Contracts.Online;

namespace API.Application.Features.Status;

public class StatusHandlers(
    GetStatusQueryHandler getStatus,
    SetStatusCommandHandler setStatus)
    : IStatusHandlers
{
    public IQueryHandler<GetStatusQuery, Result<UserStatusDto>> GetStatus => getStatus;
    public ICommandHandler<SetStatusCommand> SetStatus => setStatus;
}