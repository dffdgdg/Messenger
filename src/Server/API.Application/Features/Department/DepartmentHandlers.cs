using API.Application.Common;
using API.Application.Features.Department.Commands;
using API.Application.Features.Department.Queries;
using API.Domain.Common;
using Shared.Contracts.Department;
using Shared.Contracts.User;

namespace API.Application.Features.Chat;

public class DepartmentHandlers(
    AddUserToDepartmentCommandHandler addUserToDepartment,
    CreateDepartmentCommandHandler createDepartment,
    DeleteDepartmentCommandHandler deleteDepartment,
    RemoveUserFromDepartmentCommandHandler removeUser,
    UpdateDepartmentCommandHandler updateDepartment,
    CanManageDepartmentQueryHandler canManage,
    GetDepartmentMembersQueryHandler getMembers,
    GetDepartmentQueryHandler getDepartment,
    GetDepartmentsQueryHandler getDepartments)
    : IDepartmentHandlers
{
    public ICommandHandler<AddUserToDepartmentCommand> AddUserToDepartment => addUserToDepartment;
    public ICommandHandler<CreateDepartmentCommand, DepartmentDto> CreateDepartment => createDepartment;
    public ICommandHandler<DeleteDepartmentCommand> DeleteDepartment => deleteDepartment;
    public ICommandHandler<RemoveUserFromDepartmentCommand> RemoveUser => removeUser;
    public ICommandHandler<UpdateDepartmentCommand> UpdateDepartment => updateDepartment;
    public IQueryHandler<CanManageDepartmentQuery, Result<bool>> CanManage => canManage;
    public IQueryHandler<GetDepartmentMembersQuery, Result<List<UserDto>>> GetMembers => getMembers;
    public IQueryHandler<GetDepartmentQuery, Result<DepartmentDto>> GetDepartment => getDepartment;
    public IQueryHandler<GetDepartmentsQuery, Result<List<DepartmentDto>>> GetDepartments => getDepartments;
}