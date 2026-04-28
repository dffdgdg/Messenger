using MessengerShared.Dto.Online;

namespace MessengerAPI.Controllers;

[ApiController]
[Route("api/status")]
[Authorize]
public sealed class StatusController(IUserStatusService status, ILogger<StatusController> logger)
    : BaseController<StatusController>(logger)
{
    [HttpPost]
    public async Task<IActionResult> SetStatus([FromBody] SetStatusRequest request)
        => Map(await status.SetStatusAsync(GetCurrentUserId(), request.StatusType, request.Duration.Parse()));

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentStatus()
        => Map(await status.GetStatusAsync(GetCurrentUserId()));

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserStatus(int userId)
        => Map(await status.GetStatusAsync(userId));
}