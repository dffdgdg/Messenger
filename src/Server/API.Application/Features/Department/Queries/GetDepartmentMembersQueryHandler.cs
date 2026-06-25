using API.Application.Common;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.Department.Queries;

public class GetDepartmentMembersQueryHandler(IDepartmentRepository departmentRepository, IUserRepository userRepository)
    : IQueryHandler<GetDepartmentMembersQuery, Result<List<UserDto>>>
{
    public virtual async Task<Result<List<UserDto>>> HandleAsync(GetDepartmentMembersQuery query, CancellationToken ct = default)
    {
        var exists = await departmentRepository.ExistsAsync(query.DepartmentId, ct);
        if (!exists)
            return Result<List<UserDto>>.NotFound($"Отдел с ID {query.DepartmentId} не найден");

        var userIds = await userRepository.GetUserIdsByDepartmentAsync(query.DepartmentId, ct);
        var users = await userRepository.GetByIdsAsync(userIds, ct);

        var result = users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            DisplayName = u.GetDisplayName(),
            Surname = u.Surname,
            Name = u.Name,
            Midname = u.Midname,
            Avatar = u.Avatar,
            DepartmentId = u.DepartmentId,
            Department = u.Department?.Name
        }).ToList();

        return Result<List<UserDto>>.Success(result);
    }
}

