using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Online;
using Shared.Contracts.User;
using Shared.Enum;

namespace API.Application.Features.User.Queries;

public class GetUserStatusQueryHandler(IUserRepository userRepository, IOnlineUserService onlineService)
    : IQueryHandler<GetUserStatusQuery, Result<UserStatusDto>>
{
    public virtual async Task<Result<UserStatusDto>> HandleAsync(GetUserStatusQuery query, CancellationToken ct = default)
    {
        var user = await userRepository.GetWithSettingsAsync(query.UserId, ct);
        var isOnline = onlineService.IsOnline(query.UserId);

        return Result<UserStatusDto>.Success(new UserStatusDto(
            query.UserId,
            isOnline,
            user?.LastOnline,
            isOnline ? user?.StatusType ?? UserStatusType.Online : UserStatusType.Online,
            user?.StatusExpiresAt));
    }
}

