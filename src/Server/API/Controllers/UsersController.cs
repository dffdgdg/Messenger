using API.Application.Services.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Shared.Dto.User;

namespace API.Web.Controllers;

public sealed class UsersController(IUserService user, ILogger<UsersController> logger) : BaseController<UsersController>(logger)
{
    [HttpGet]
    public async Task<IActionResult> GetAllUsers(CancellationToken ct)
        => Map(await user.GetAllUsersAsync(ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id, CancellationToken ct)
        => Map(await user.GetUserAsync(id, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDto userDto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return Map(await user.UpdateUserAsync(id, userDto, ct));
    }

    [HttpPost("{id}/avatar")]
    public async Task<IActionResult> UploadAvatar(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden<AvatarResponseDto>();
        return Map(await user.UploadAvatarAsync(id, file, ct));
    }

    [HttpDelete("{id}/avatar")]
    public async Task<IActionResult> RemoveAvatar(int id, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return Map(await user.RemoveAvatarAsync(id, ct));
    }

    [HttpPut("{id}/username")]
    public async Task<IActionResult> ChangeUsername(int id, [FromBody] ChangeUsernameDto dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return Map(await user.ChangeUsernameAsync(id, dto, ct));
    }

    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return Map(await user.ChangePasswordAsync(id, dto, ct));
    }

    [HttpGet("online")]
    public async Task<IActionResult> GetOnlineUsers(CancellationToken ct)
        => Map(await user.GetOnlineUsersAsync(ct));

    [HttpGet("{id}/status")]
    public async Task<IActionResult> GetUserOnlineStatus(int id, CancellationToken ct)
        => Map(await user.GetOnlineStatusAsync(id, ct));

    [HttpPost("status/batch")]
    public async Task<IActionResult> GetUsersOnlineStatus([FromBody] List<int> userIds, CancellationToken ct)
        => Map(await user.GetOnlineStatusesAsync(userIds, ct));
}