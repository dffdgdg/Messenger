using API.Application.Common;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Department;

namespace API.Application.Features.Department.Queries;

public class GetDepartmentsQueryHandler(IDepartmentRepository departmentRepository)
    : IQueryHandler<GetDepartmentsQuery, Result<List<DepartmentDto>>>
{
    public virtual async Task<Result<List<DepartmentDto>>> HandleAsync(GetDepartmentsQuery query, CancellationToken ct = default)
    {
        var departments = await departmentRepository.GetAllWithHeadAsync(ct);
        var userCounts = await departmentRepository.GetUserCountsAsync(ct);

        var result = departments.ConvertAll(d => new DepartmentDto
        {
            Id = d.Id,
            Name = d.Name,
            ParentDepartmentId = d.ParentDepartmentId,
            Head = d.HeadId,
            HeadName = d.Head?.GetDisplayName(),
            UserCount = userCounts.GetValueOrDefault(d.Id, 0)
        });

        return Result<List<DepartmentDto>>.Success(result);
    }
}

