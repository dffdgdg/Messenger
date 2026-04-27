using MessengerShared.Dto.Online;
using System.Security.Claims;

namespace MessengerAPI.Controllers;

[ApiController]
[Route("api/status")]
[Authorize]
public class StatusController(IUserStatusService statusService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SetStatus([FromBody] SetStatusRequest request)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        TimeSpan? duration = request.Duration switch
        {
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            "2h" => TimeSpan.FromHours(2),
            "4h" => TimeSpan.FromHours(4),
            "8h" => TimeSpan.FromHours(8),
            "24h" => TimeSpan.FromHours(24),
            _ => null
        };

        await statusService.SetStatusAsync(userId, request.StatusType, duration);
        return Ok();
    }

    [HttpGet("current")]
    public async Task<ActionResult<UserStatusDto>> GetCurrentStatus()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        return Ok(await statusService.GetStatusAsync(userId));
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<UserStatusDto>> GetUserStatus(int userId)
        => Ok(await statusService.GetStatusAsync(userId));
}