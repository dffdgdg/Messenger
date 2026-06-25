using API.Application.Common;
using API.Application.Features.Department.Commands;
using API.Application.Features.Department.Queries;
using API.Domain.Common;
using Shared.Contracts.Department;
using Shared.Contracts.User;

namespace API.Application.Features.Chat;

public interface IDepartmentHandlers
{
    ICommandHandler<AddUserToDepartmentCommand> AddUserToDepartment { get; }
    ICommandHandler<CreateDepartmentCommand, DepartmentDto> CreateDepartment { get; }
    ICommandHandler<DeleteDepartmentCommand> DeleteDepartment { get; }
    ICommandHandler<RemoveUserFromDepartmentCommand> RemoveUser { get; }
    ICommandHandler<UpdateDepartmentCommand> UpdateDepartment { get; }
    IQueryHandler<CanManageDepartmentQuery, Result<bool>> CanManage { get; }
    IQueryHandler<GetDepartmentMembersQuery, Result<List<UserDto>>> GetMembers { get; }
    IQueryHandler<GetDepartmentQuery, Result<DepartmentDto>> GetDepartment { get; }
    IQueryHandler<GetDepartmentsQuery, Result<List<DepartmentDto>>> GetDepartments { get; }
}