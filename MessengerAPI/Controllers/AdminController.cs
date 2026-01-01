using MessengerAPI.Services;
using MessengerShared.DTO.User;
using MessengerShared.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController(IAdminService adminService, ILogger<AdminController> logger) : BaseController<AdminController>(logger)
    {
        [HttpGet("users")]
        public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetUsers(CancellationToken ct)
            => await ExecuteAsync(async () => await adminService.GetUsersAsync(ct), "Пользователи получены успешно");

        [HttpPost("users")]
        public async Task<ActionResult<ApiResponse<UserDTO>>> CreateUser([FromBody] CreateUserDTO dto, CancellationToken ct) => await ExecuteAsync(async () =>
        {
            ValidateModel();

            return await adminService.CreateUserAsync(dto, ct);
        }, "Пользователь создан успешно");

        [HttpPost("users/{id}/toggle-ban")]
        public async Task<IActionResult> ToggleBan(int id, CancellationToken ct)
            => await ExecuteAsync(async () => await adminService.ToggleBanAsync(id, ct), "Статус блокировки изменён");
    }
}