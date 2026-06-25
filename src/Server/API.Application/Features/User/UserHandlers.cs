using API.Application.Common;
using API.Application.Features.User.Commands;
using API.Application.Features.User.Queries;
using API.Domain.Common;
using Shared.Contracts.Online;
using Shared.Contracts.User;

namespace API.Application.Features.User;

public class UserHandlers(
    ChangePasswordCommandHandler changePassword,
    ChangeUsernameCommandHandler changeUsername,
    RemoveUserAvatarCommandHandler removeAvatar,
    UpdateUserCommandHandler updateUser,
    UploadUserAvatarCommandHandler uploadAvatar,
    GetAllUsersQueryHandler getUsers,
    GetUserQueryHandler getUser,
    GetOnlineUsersQueryHandler getOnlineUsers,
    GetUserStatusQueryHandler getUserStatus,
    GetUserStatusesQueryHandler getUserStatuses)
    : IUserHandlers
{
    public ICommandHandler<ChangePasswordCommand> ChangePassword => changePassword;
    public ICommandHandler<ChangeUsernameCommand> ChangeUsername => changeUsername;
    public ICommandHandler<RemoveUserAvatarCommand> RemoveAvatar => removeAvatar;
    public ICommandHandler<UpdateUserCommand> UpdateUser => updateUser;
    public ICommandHandler<UploadUserAvatarCommand, AvatarResponseDto> UploadAvatar => uploadAvatar;
    public IQueryHandler<GetAllUsersQuery, Result<List<UserDto>>> GetUsers => getUsers;
    public IQueryHandler<GetUserQuery, Result<UserDto>> GetUser => getUser;
    public IQueryHandler<GetOnlineUsersQuery, Result<OnlineUsersResponseDto>> GetOnlineUsers => getOnlineUsers;
    public IQueryHandler<GetUserStatusQuery, Result<UserStatusDto>> GetUserStatus => getUserStatus;
    public IQueryHandler<GetUserStatusesQuery, Result<List<UserStatusDto>>> GetUserStatuses => getUserStatuses;
}