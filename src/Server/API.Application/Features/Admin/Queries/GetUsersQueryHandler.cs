using API.Application.Common;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.Admin.Queries;

public class GetUsersQueryHandler(IUserRepository userRepository) : IQueryHandler<GetUsersQuery, Result<List<UserDto>>>
{
    public virtual async Task<Result<List<UserDto>>> HandleAsync(GetUsersQuery query, CancellationToken ct = default)
    {
        var users = await userRepository.GetAllWithSettingsAsync(ct);

        var result = users.ConvertAll(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            DisplayName = string.IsNullOrWhiteSpace(u.Surname) ? u.Username : $"{u.Surname} {u.Name} {u.Midname}".Trim(),
            Surname = u.Surname,
            Name = u.Name,
            Midname = u.Midname,
            Avatar = u.Avatar,
            DepartmentId = u.DepartmentId,
            Department = u.DepartmentName,
            IsBanned = u.IsBanned,
            LastOnline = u.LastOnline,
            Theme = u.Theme,
            NotificationsEnabled = u.NotificationsEnabled
        });

        return Result<List<UserDto>>.Success(result);
    }
}
