using API.Application.Common;
using API.Application.Features.Admin.Commands;
using API.Application.Features.Admin.Queries;
using API.Domain.Common;
using Shared.Contracts.User;

namespace API.Application.Features.Admin;

public interface IAdminHandlers
{
    ICommandHandler<CreateUserCommand, UserDto> CreateUser { get; }
    ICommandHandler<ResetPasswordCommand> ResetPassword { get; }
    ICommandHandler<ToggleBanCommand> ToggleBan { get; }
    ICommandHandler<UpdateUserCommand, UserDto> UpdateUser { get; }
    IQueryHandler<GetUsersQuery, Result<List<UserDto>>> GetUsers { get; }
}