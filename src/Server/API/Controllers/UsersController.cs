using API.Application.Features.User;
using API.Application.Features.User.Commands;
using API.Application.Features.User.Queries;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.User;

namespace API.Web.Controllers;

public sealed class UsersController(IUserHandlers handlers, ILogger<UsersController> logger)
    : BaseController<UsersController>(logger)
{
    [HttpGet]
    public async Task<IActionResult> GetAllUsers(CancellationToken ct)
        => Map(await handlers.GetUsers.HandleAsync(new GetAllUsersQuery(), ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id, CancellationToken ct)
        => Map(await handlers.GetUser.HandleAsync(new GetUserQuery(id), ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDto dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return Map(await handlers.UpdateUser.HandleAsync(new UpdateUserCommand(id, dto), ct));
    }

    [HttpPost("{id}/avatar")]
    public async Task<IActionResult> UploadAvatar(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden<AvatarResponseDto>();
        return Map(await handlers.UploadAvatar.HandleAsync(new UploadUserAvatarCommand(id, file), ct));
    }

    [HttpDelete("{id}/avatar")]
    public async Task<IActionResult> RemoveAvatar(int id, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return Map(await handlers.RemoveAvatar.HandleAsync(new RemoveUserAvatarCommand(id), ct));
    }

    [HttpPut("{id}/username")]
    public async Task<IActionResult> ChangeUsername(int id, [FromBody] ChangeUsernameDto dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return Map(await handlers.ChangeUsername.HandleAsync(new ChangeUsernameCommand(id, dto), ct));
    }

    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return Map(await handlers.ChangePassword.HandleAsync(new ChangePasswordCommand(id, dto), ct));
    }

    [HttpGet("online")]
    public async Task<IActionResult> GetOnlineUsers(CancellationToken ct)
        => Map(await handlers.GetOnlineUsers.HandleAsync(new GetOnlineUsersQuery(), ct));

    [HttpGet("{id}/status")]
    public async Task<IActionResult> GetUserOnlineStatus(int id, CancellationToken ct)
        => Map(await handlers.GetUserStatus.HandleAsync(new GetUserStatusQuery(id), ct));

    [HttpPost("status/batch")]
    public async Task<IActionResult> GetUsersOnlineStatus([FromBody] List<int> userIds, CancellationToken ct)
        => Map(await handlers.GetUserStatuses.HandleAsync(new GetUserStatusesQuery(userIds), ct));
}