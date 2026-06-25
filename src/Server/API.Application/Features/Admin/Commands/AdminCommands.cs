using Shared.Contracts.User;

namespace API.Application.Features.Admin.Commands;

public sealed record CreateUserCommand(CreateUserDto Dto);
public sealed record UpdateUserCommand(int UserId, UserDto Dto);
public sealed record ToggleBanCommand(int UserId);
public sealed record ResetPasswordCommand(int UserId, string NewPassword);
