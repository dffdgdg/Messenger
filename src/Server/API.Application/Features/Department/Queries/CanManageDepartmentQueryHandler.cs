using API.Application.Common;
using API.Application.Configuration;
using API.Domain.Common;
using API.Domain.Repositories;
using Microsoft.Extensions.Options;
using Shared.Enum;

namespace API.Application.Features.Department.Queries;

public class CanManageDepartmentQueryHandler(IDepartmentRepository departmentRepository, IOptions<MessengerSettings> settings)
    : IQueryHandler<CanManageDepartmentQuery, Result<bool>>
{
    private readonly MessengerSettings _settings = settings.Value;

    public virtual async Task<Result<bool>> HandleAsync(CanManageDepartmentQuery query, CancellationToken ct = default)
    {
        var canManage = await CanManageAsync(query.UserId, query.DepartmentId, ct);
        return Result<bool>.Success(canManage);
    }

    internal async Task<bool> CanManageAsync(int userId, int departmentId, CancellationToken ct)
    {
        if (await IsAdminAsync(userId, ct))
            return true;

        return await departmentRepository.IsHeadOfDepartmentAsync(userId, departmentId, ct);
    }

    internal async Task<bool> IsAdminAsync(int userId, CancellationToken ct)
    {
        var role = await departmentRepository.ResolveUserRoleAsync(userId, _settings.AdminDepartmentId, ct);

        return role.HasFlag(UserRole.Admin);
    }
}

