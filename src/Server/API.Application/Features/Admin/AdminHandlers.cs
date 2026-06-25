using API.Application.Common;
using API.Application.Features.Admin.Commands;
using API.Application.Features.Admin.Queries;
using API.Domain.Common;
using Shared.Contracts.User;

namespace API.Application.Features.Admin;

public class AdminHandlers(
    CreateUserCommandHandler createUser,
    ResetPasswordCommandHandler resetPassword,
    ToggleBanCommandHandler toggleBan,
    UpdateUserCommandHandler updateUser,
    GetUsersQueryHandler getUsers)
    : IAdminHandlers
{
    public ICommandHandler<CreateUserCommand, UserDto> CreateUser => createUser;
    public ICommandHandler<ResetPasswordCommand> ResetPassword => resetPassword;
    public ICommandHandler<ToggleBanCommand> ToggleBan => toggleBan;
    public ICommandHandler<UpdateUserCommand, UserDto> UpdateUser => updateUser;
    public IQueryHandler<GetUsersQuery, Result<List<UserDto>>> GetUsers => getUsers;
}