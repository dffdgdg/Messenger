using API.Application.Common;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Department;

namespace API.Application.Features.Department.Queries;

public class GetDepartmentQueryHandler(IDepartmentRepository departmentRepository)
    : IQueryHandler<GetDepartmentQuery, Result<DepartmentDto>>
{
    public virtual async Task<Result<DepartmentDto>> HandleAsync(GetDepartmentQuery query, CancellationToken ct = default)
    {
        var department = await departmentRepository.FindByIdWithHeadAsync(query.DepartmentId, ct);
        if (department is null)
            return Result<DepartmentDto>.NotFound($"Отдел с ID {query.DepartmentId} не найден");

        var userCount = await departmentRepository.CountUsersAsync(query.DepartmentId, ct);

        return Result<DepartmentDto>.Success(new DepartmentDto
        {
            Id = department.Id,
            Name = department.Name,
            ParentDepartmentId = department.ParentDepartmentId,
            Head = department.HeadId,
            HeadName = department.Head?.GetDisplayName(),
            UserCount = userCount
        });
    }
}

