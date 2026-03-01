using MessengerAPI.Services.User;

namespace MessengerAPI.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController(IAdminService adminService, ILogger<AdminController> logger)
    : BaseController<AdminController>(logger)
{
    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetUsers(CancellationToken ct)
        => await ExecuteAsync(() => adminService.GetUsersAsync(ct), "Пользователи получены успешно");

    [HttpPost("users")]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUser([FromBody] CreateUserDto dto, CancellationToken ct)
        => await ExecuteAsync(() => adminService.CreateUserAsync(dto, ct), "Пользователь создан успешно");

    [HttpPost("users/{id}/toggle-ban")]
    public async Task<IActionResult> ToggleBan(int id, CancellationToken ct)
        => await ExecuteAsync(() => adminService.ToggleBanAsync(id, ct), "Статус блокировки изменён");
}