using MessengerAPI.Services.User;

namespace MessengerAPI.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(IAdminService adminService, ILogger<AdminController> logger) : BaseController<AdminController>(logger)
{
    private readonly IAdminService _admin = adminService;

    [HttpGet("users")]
    public Task<ActionResult<ApiResponse<List<UserDto>>>> GetUsers(CancellationToken ct) => ExecuteAsync(()
        => _admin.GetUsersAsync(ct));

    [HttpPost("users")]
    public Task<ActionResult<ApiResponse<UserDto>>> CreateUser([FromBody] CreateUserDto dto, CancellationToken ct) => ExecuteAsync(()
        => _admin.CreateUserAsync(dto, ct));

    [HttpPut("users/{id:int}")]
    public Task<ActionResult<ApiResponse<UserDto>>> UpdateUser(int id,[FromBody] UserDto dto,CancellationToken ct) => ExecuteAsync(()
        => _admin.UpdateUserAsync(id, dto, ct));

    [HttpPost("users/{id:int}/toggle-ban")]
    public Task<IActionResult> ToggleBan(int id, CancellationToken ct) => ExecuteAsync(()
        => _admin.ToggleBanAsync(id, ct));

    [HttpPost("users/{id:int}/reset-password")]
    public Task<IActionResult> ResetPassword(int id,[FromBody] ResetPasswordAdminDto dto,CancellationToken ct) => ExecuteAsync(()
        => _admin.ResetPasswordAsync(id, dto.NewPassword, ct));
}