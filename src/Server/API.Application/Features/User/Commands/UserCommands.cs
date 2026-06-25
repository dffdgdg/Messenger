using Microsoft.AspNetCore.Http;
using Shared.Contracts.User;

namespace API.Application.Features.User.Commands;

public sealed record UpdateUserCommand(int UserId, UserDto Dto);
public sealed record UploadUserAvatarCommand(int UserId, IFormFile File);
public sealed record RemoveUserAvatarCommand(int UserId);
public sealed record ChangeUsernameCommand(int UserId, ChangeUsernameDto Dto);
public sealed record ChangePasswordCommand(int UserId, ChangePasswordDto Dto);
