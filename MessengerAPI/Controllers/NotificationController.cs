using MessengerAPI.Services.Chat;
using MessengerShared.DTO.Chat;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class NotificationController(INotificationService notificationService,IChatService chatService, ILogger<NotificationController> logger)
    : BaseController<NotificationController>(logger)
{
    [HttpGet("chat/{chatId}/settings")]
    public async Task<ActionResult<ApiResponse<ChatNotificationSettingsDTO>>> GetChatSettings(int chatId)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, chatId);
            var settings = await notificationService.GetChatNotificationSettingsAsync(userId, chatId);
            return settings ?? throw new KeyNotFoundException("Настройки не найдены");
        });
    }

    [HttpPost("chat/mute")]
    public async Task<ActionResult<ApiResponse<ChatNotificationSettingsDTO>>> SetChatMute([FromBody] ChatNotificationSettingsDTO request)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            ValidateModel();
            await chatService.EnsureUserHasChatAccessAsync(userId, request.ChatId);
            return await notificationService.SetChatMuteAsync(userId, request);
        }, request.NotificationsEnabled ? "Уведомления включены" : "Уведомления отключены");
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<List<ChatNotificationSettingsDTO>>>>GetAllSettings()
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(() => notificationService.GetAllChatSettingsAsync(userId),"Настройки получены");
    }
}