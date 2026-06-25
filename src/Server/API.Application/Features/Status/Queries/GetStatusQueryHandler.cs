using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using Shared.Contracts.Online;
using Shared.Contracts.User;

namespace API.Application.Features.Status.Queries;

public class GetStatusQueryHandler(IUserStatusService userStatusService)
    : IQueryHandler<GetStatusQuery, Result<UserStatusDto>>
{
    public virtual async Task<Result<UserStatusDto>> HandleAsync(GetStatusQuery query, CancellationToken ct = default)
        => await userStatusService.GetStatusAsync(query.UserId);
}

