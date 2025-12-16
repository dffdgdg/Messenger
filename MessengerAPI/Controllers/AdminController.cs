using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController(IAdminService adminService, ILogger<AdminController> logger) : BaseController<AdminController>(logger)
    {
        [HttpGet]
        public ActionResult<ApiResponse<string>> Get() => Success<string>("Admin controller работает");

        [HttpGet("users")]
        public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetUsers()
            => await ExecuteAsync(() => adminService.GetUsersAsync(), "ѕользователи получены успешно");
    }
}