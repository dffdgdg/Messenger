using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.User.Queries;

public class GetUserQueryHandler(IUserRepository userRepository, IOnlineUserService onlineService, GetAllUsersQueryHandler mapper)
    : IQueryHandler<GetUserQuery, Result<UserDto>>
{
    public virtual async Task<Result<UserDto>> HandleAsync(GetUserQuery query, CancellationToken ct = default)
    {
        var user = await userRepository.GetWithSettingsAsync(query.UserId, ct);
        if (user is null)
            return Result<UserDto>.NotFound($"Пользователь с ID {query.UserId} не найден");

        return Result<UserDto>.Success(mapper.MapToDto(user, onlineService.IsOnline(query.UserId)));
    }
}

