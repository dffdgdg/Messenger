using MessengerAPI.Services.Chat;

namespace MessengerAPI.Controllers;

public sealed class NotificationsController(INotificationService notificationService, ILogger<NotificationsController> logger)
    : BaseController<NotificationsController>(logger)
{
    [HttpGet("chat/{chatId}/settings")]
    public async Task<ActionResult<ApiResponse<ChatNotificationSettingsDto>>> GetChatSettings(int chatId)
        => await ExecuteAsync(() => notificationService.GetChatNotificationSettingsAsync(GetCurrentUserId(), chatId));

    [HttpPost("chat/mute")]
    public async Task<ActionResult<ApiResponse<ChatNotificationSettingsDto>>> SetChatMute([FromBody] ChatNotificationSettingsDto request)
        => await ExecuteAsync(() => notificationService.SetChatMuteAsync(GetCurrentUserId(), request));

    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<List<ChatNotificationSettingsDto>>>> GetAllSettings()
        => await ExecuteAsync(() => notificationService.GetAllChatSettingsAsync(GetCurrentUserId()));
}