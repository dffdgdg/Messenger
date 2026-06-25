using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Online;
using Shared.Contracts.User;

namespace API.Application.Features.User.Queries;

public class GetUserStatusesQueryHandler(IUserRepository userRepository, IOnlineUserService onlineService)
    : IQueryHandler<GetUserStatusesQuery, Result<List<UserStatusDto>>>
{
    public virtual async Task<Result<List<UserStatusDto>>> HandleAsync(GetUserStatusesQuery query, CancellationToken ct = default)
    {
        if (query.UserIds is not { Count: > 0 })
            return Result<List<UserStatusDto>>.Failure("Список ID пользователей не может быть пустым");

        var users = await userRepository.GetByIdsAsync(query.UserIds, ct);
        var onlineIds = onlineService.FilterOnline(query.UserIds);

        return Result<List<UserStatusDto>>.Success(users.ConvertAll(u => new UserStatusDto(u.Id, onlineIds.Contains(u.Id), u.LastOnline)));
    }
}

