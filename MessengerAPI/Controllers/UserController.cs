using MessengerAPI.Services.User;
using MessengerShared.DTO.Online;
using MessengerShared.DTO.User;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class UserController(IUserService userService,ILogger<UserController> logger) : BaseController<UserController>(logger)
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetAllUsers(CancellationToken ct)
        => await ExecuteResultAsync(() => userService.GetAllUsersAsync(ct), "Пользователи получены успешно");

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserDTO>>> GetUser(int id, CancellationToken ct)
        => await ExecuteResultAsync(() => userService.GetUserAsync(id, ct));

    [HttpGet("online")]
    public async Task<ActionResult<ApiResponse<OnlineUsersResponseDTO>>> GetOnlineUsers(CancellationToken ct)
        => await ExecuteResultAsync(async () =>
        {
            var idsResult = await userService.GetOnlineUserIdsAsync(ct);
            if (idsResult.IsFailure)
                return Common.Result<OnlineUsersResponseDTO>.Failure(idsResult.Error!);

            return Common.Result<OnlineUsersResponseDTO>.Success(new OnlineUsersResponseDTO
            {
                OnlineUserIds = idsResult.Value!,
                TotalOnline = idsResult.Value!.Count
            });
        }, "Список онлайн пользователей получен");

    [HttpGet("{id}/status")]
    public async Task<ActionResult<ApiResponse<OnlineStatusDTO>>> GetUserOnlineStatus(int id, CancellationToken ct)
        => await ExecuteResultAsync(() => userService.GetOnlineStatusAsync(id, ct));

    [HttpPost("status/batch")]
    public async Task<ActionResult<ApiResponse<List<OnlineStatusDTO>>>> GetUsersOnlineStatus([FromBody] List<int> userIds, CancellationToken ct)
        => await ExecuteResultAsync(() => userService.GetOnlineStatusesAsync(userIds, ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDTO userDto, CancellationToken ct)
        => await ExecuteResultAsync(() => userService.UpdateUserAsync(id, userDto, ct), "Пользователь обновлён успешно");

    [HttpPost("{id}/avatar")]
    public async Task<ActionResult<ApiResponse<AvatarResponseDTO>>> UploadAvatar(int id, IFormFile file, CancellationToken ct)
        => await ExecuteResultAsync(async () =>
        {
            var result = await userService.UploadAvatarAsync(id, file, ct);
            if (result.IsFailure)
                return Common.Result<AvatarResponseDTO>.Failure(result.Error!);

            return Common.Result<AvatarResponseDTO>.Success(new AvatarResponseDTO { AvatarUrl = result.Value! });
        }, "Аватар загружен успешно");

    [HttpPut("{id}/username")]
    public async Task<IActionResult> ChangeUsername(int id, [FromBody] ChangeUsernameDTO dto, CancellationToken ct)
        => await ExecuteResultAsync(() => userService.ChangeUsernameAsync(id, dto, ct), "Username успешно изменён");

    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDTO dto, CancellationToken ct)
        => await ExecuteResultAsync(() => userService.ChangePasswordAsync(id, dto, ct), "Пароль успешно изменён");
}