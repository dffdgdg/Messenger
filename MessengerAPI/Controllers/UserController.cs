using MessengerAPI.Services.User;
using MessengerShared.Dto.Online;

namespace MessengerAPI.Controllers;

public sealed class UserController(IUserService userService, ILogger<UserController> logger)
    : BaseController<UserController>(logger)
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetAllUsers(CancellationToken ct)
        => await ExecuteAsync(() => userService.GetAllUsersAsync(ct),"Пользователи получены успешно");

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(int id, CancellationToken ct)
        => await ExecuteAsync(() => userService.GetUserAsync(id, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDto userDto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return await ExecuteAsync(() => userService.UpdateUserAsync(id, userDto, ct),"Пользователь обновлён успешно");
    }

    [HttpPost("{id}/avatar")]
    public async Task<ActionResult<ApiResponse<AvatarResponseDto>>> UploadAvatar(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden<AvatarResponseDto>();
        return await ExecuteAsync(() => userService.UploadAvatarAsync(id, file, ct), "Аватар загружен успешно");
    }

    [HttpPut("{id}/username")]
    public async Task<IActionResult> ChangeUsername(int id, [FromBody] ChangeUsernameDto dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return await ExecuteAsync(() => userService.ChangeUsernameAsync(id, dto, ct), "Username успешно изменён");
    }

    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto dto, CancellationToken ct)
    {
        if (!IsCurrentUser(id)) return Forbidden();
        return await ExecuteAsync(() => userService.ChangePasswordAsync(id, dto, ct), "Пароль успешно изменён");
    }

    [HttpGet("online")]
    public async Task<ActionResult<ApiResponse<OnlineUsersResponseDto>>> GetOnlineUsers(CancellationToken ct)
        => await ExecuteAsync(() => userService.GetOnlineUsersAsync(ct), "Список онлайн пользователей получен");

    [HttpGet("{id}/status")]
    public async Task<ActionResult<ApiResponse<OnlineStatusDto>>> GetUserOnlineStatus(int id, CancellationToken ct)
        => await ExecuteAsync(() => userService.GetOnlineStatusAsync(id, ct));

    [HttpPost("status/batch")]
    public async Task<ActionResult<ApiResponse<List<OnlineStatusDto>>>> GetUsersOnlineStatus([FromBody] List<int> userIds, CancellationToken ct)
        => await ExecuteAsync(() => userService.GetOnlineStatusesAsync(userIds, ct));
}