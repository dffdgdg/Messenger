using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using Shared.Contracts.Online;

namespace API.Application.Features.User.Queries;

public class GetOnlineUsersQueryHandler(IOnlineUserService onlineService)
    : IQueryHandler<GetOnlineUsersQuery, Result<OnlineUsersResponseDto>>
{
    public Task<Result<OnlineUsersResponseDto>> HandleAsync(GetOnlineUsersQuery query, CancellationToken ct = default)
    {
        var onlineIds = onlineService.GetOnlineUserIds();
        return Task.FromResult(Result<OnlineUsersResponseDto>.Success(new OnlineUsersResponseDto
        {
            OnlineUserIds = [.. onlineIds],
            TotalOnline = onlineIds.Count
        }));
    }
}
