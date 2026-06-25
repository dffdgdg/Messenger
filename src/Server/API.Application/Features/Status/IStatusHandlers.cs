using API.Application.Common;
using API.Application.Features.Status.Commands;
using API.Application.Features.Status.Queries;
using API.Domain.Common;
using Shared.Contracts.Online;

namespace API.Application.Features.Status;

public interface IStatusHandlers
{
    IQueryHandler<GetStatusQuery, Result<UserStatusDto>> GetStatus { get; }
    ICommandHandler<SetStatusCommand> SetStatus { get; }
}