namespace API.Controllers;

public sealed class NotificationsController(INotificationService notification, ILogger<NotificationsController> logger) : BaseController<NotificationsController>(logger)
{
    [HttpGet("chat/{chatId}/settings")]
    public async Task<IActionResult> GetChatSettings(int chatId)
        => Map(await notification.GetChatNotificationSettingsAsync(GetCurrentUserId(), chatId));

    [HttpPost("chat/mute")]
    public async Task<IActionResult> SetChatMute([FromBody] ChatNotificationSettingsDto request)
        => Map(await notification.SetChatMuteAsync(GetCurrentUserId(), request));

    [HttpGet("settings")]
    public async Task<IActionResult> GetAllSettings()
        => Map(await notification.GetAllChatSettingsAsync(GetCurrentUserId()));
}