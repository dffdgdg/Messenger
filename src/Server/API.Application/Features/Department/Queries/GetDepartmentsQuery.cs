namespace API.Application.Features.Department.Queries;

public sealed record GetDepartmentsQuery;
public sealed record GetDepartmentQuery(int DepartmentId);
public sealed record GetDepartmentMembersQuery(int DepartmentId);
public sealed record CanManageDepartmentQuery(int UserId, int DepartmentId);
