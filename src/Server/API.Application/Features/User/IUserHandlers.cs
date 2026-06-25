using API.Application.Common;
using API.Application.Features.User.Commands;
using API.Application.Features.User.Queries;
using API.Domain.Common;
using Shared.Contracts.User;
using Shared.Contracts.Online;

namespace API.Application.Features.User;

public interface IUserHandlers
{
    ICommandHandler<ChangePasswordCommand> ChangePassword { get; }
    ICommandHandler<ChangeUsernameCommand> ChangeUsername { get; }
    ICommandHandler<RemoveUserAvatarCommand> RemoveAvatar { get; }
    ICommandHandler<UpdateUserCommand> UpdateUser { get; }
    ICommandHandler<UploadUserAvatarCommand, AvatarResponseDto> UploadAvatar { get; }
    IQueryHandler<GetAllUsersQuery, Result<List<UserDto>>> GetUsers { get; }
    IQueryHandler<GetUserQuery, Result<UserDto>> GetUser { get; }
    IQueryHandler<GetOnlineUsersQuery, Result<OnlineUsersResponseDto>> GetOnlineUsers { get; }
    IQueryHandler<GetUserStatusQuery, Result<UserStatusDto>> GetUserStatus { get; }
    IQueryHandler<GetUserStatusesQuery, Result<List<UserStatusDto>>> GetUserStatuses { get; }
}