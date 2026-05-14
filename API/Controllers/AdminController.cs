namespace API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class AdminController(IAdminService admin, ILogger<AdminController> logger) : BaseController<AdminController>(logger)
{
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
        => Map(await admin.GetUsersAsync(ct));

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto, CancellationToken ct)
        => Map(await admin.CreateUserAsync(dto, ct));

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDto dto, CancellationToken ct)
        => Map(await admin.UpdateUserAsync(id, dto, ct));

    [HttpPost("users/{id:int}/toggle-ban")]
    public async Task<IActionResult> ToggleBan(int id, CancellationToken ct)
        => Map(await admin.ToggleBanAsync(id, ct));

    [HttpPost("users/{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordAdminDto dto, CancellationToken ct)
        => Map(await admin.ResetPasswordAsync(id, dto.NewPassword, ct));
}