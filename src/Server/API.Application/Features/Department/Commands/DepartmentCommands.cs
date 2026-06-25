using Shared.Contracts.Department;

namespace API.Application.Features.Department.Commands;

public sealed record CreateDepartmentCommand(DepartmentDto Dto);
public sealed record UpdateDepartmentCommand(int DepartmentId, DepartmentDto Dto);
public sealed record DeleteDepartmentCommand(int DepartmentId);
public sealed record AddUserToDepartmentCommand(int DepartmentId, int UserId, int RequesterId);
public sealed record RemoveUserFromDepartmentCommand(int DepartmentId, int UserId, int RequesterId);
