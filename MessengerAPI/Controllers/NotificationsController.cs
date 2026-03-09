using MessengerAPI.Services.Chat;

namespace MessengerAPI.Controllers;

public sealed class NotificationsController(INotificationService notificationService, ILogger<NotificationsController> logger)
    : BaseController<NotificationsController>(logger)
{
    [HttpGet("chat/{chatId}/settings")]
    public async Task<ActionResult<ApiResponse<ChatNotificationSettingsDto>>> GetChatSettings(int chatId)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => notificationService.GetChatNotificationSettingsAsync(userId, chatId));
    }

    [HttpPost("chat/mute")]
    public async Task<ActionResult<ApiResponse<ChatNotificationSettingsDto>>> SetChatMute([FromBody] ChatNotificationSettingsDto request)
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => notificationService.SetChatMuteAsync(userId, request));
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<List<ChatNotificationSettingsDto>>>> GetAllSettings()
    {
        var userId = GetCurrentUserId();
        return await ExecuteAsync(() => notificationService.GetAllChatSettingsAsync(userId));
    }
}