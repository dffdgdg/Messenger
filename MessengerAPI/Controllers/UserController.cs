using MessengerAPI.Services.User;
using MessengerShared.DTO.Chat;
using MessengerShared.DTO.Online;
using MessengerShared.DTO.User;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class UserController(
    IUserService userService,
    ILogger<UserController> logger)
    : BaseController<UserController>(logger)
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetAllUsers(CancellationToken ct)
        => await ExecuteAsync(() => userService.GetAllUsersAsync(ct),"Пользователи получены успешно");

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserDTO>>> GetUser(int id, CancellationToken ct)
        => await ExecuteAsync(() => userService.GetUserAsync(id, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDTO userDto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return await ExecuteAsync(() => userService.UpdateUserAsync(id, userDto, ct),"Пользователь обновлён успешно");
    }

    [HttpPost("{id}/avatar")]
    public async Task<ActionResult<ApiResponse<AvatarResponseDTO>>> UploadAvatar(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden<AvatarResponseDTO>();
        return await ExecuteAsync(() => userService.UploadAvatarAsync(id, file, ct), "Аватар загружен успешно");
    }

    [HttpPut("{id}/username")]
    public async Task<IActionResult> ChangeUsername(int id, [FromBody] ChangeUsernameDTO dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return await ExecuteAsync(() => userService.ChangeUsernameAsync(id, dto, ct), "Username успешно изменён");
    }

    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDTO dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return await ExecuteAsync(() => userService.ChangePasswordAsync(id, dto, ct), "Пароль успешно изменён");
    }

    [HttpGet("online")]
    public async Task<ActionResult<ApiResponse<OnlineUsersResponseDTO>>> GetOnlineUsers(CancellationToken ct)
        => await ExecuteAsync(() => userService.GetOnlineUsersAsync(ct), "Список онлайн пользователей получен");

    [HttpGet("{id}/status")]
    public async Task<ActionResult<ApiResponse<OnlineStatusDTO>>> GetUserOnlineStatus(int id, CancellationToken ct)
        => await ExecuteAsync(() => userService.GetOnlineStatusAsync(id, ct));

    [HttpPost("status/batch")]
    public async Task<ActionResult<ApiResponse<List<OnlineStatusDTO>>>> GetUsersOnlineStatus([FromBody] List<int> userIds, CancellationToken ct)
        => await ExecuteAsync(() => userService.GetOnlineStatusesAsync(userIds, ct));
}