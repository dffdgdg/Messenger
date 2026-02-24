using MessengerAPI.Common;
using MessengerAPI.Services.Chat;
using MessengerShared.Dto.Chat;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class NotificationController(
    INotificationService notificationService,
    IChatService chatService,
    ILogger<NotificationController> logger)
    : BaseController<NotificationController>(logger)
{
    [HttpGet("chat/{chatId}/settings")]
    public async Task<ActionResult<ApiResponse<ChatNotificationSettingsDto>>> GetChatSettings(int chatId)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, chatId);
            var settings = await notificationService.GetChatNotificationSettingsAsync(userId, chatId);

            return settings is not null
                ? Result<ChatNotificationSettingsDto>.Success(settings)
                : Result<ChatNotificationSettingsDto>.Failure("Настройки не найдены");
        });
    }

    [HttpPost("chat/mute")]
    public async Task<ActionResult<ApiResponse<ChatNotificationSettingsDto>>> SetChatMute([FromBody] ChatNotificationSettingsDto request)
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(userId, request.ChatId);
            var result = await notificationService.SetChatMuteAsync(userId, request);
            return Result<ChatNotificationSettingsDto>.Success(result);
        }, request.NotificationsEnabled
            ? "Уведомления включены"
            : "Уведомления отключены");
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<List<ChatNotificationSettingsDto>>>> GetAllSettings()
    {
        var userId = GetCurrentUserId();

        return await ExecuteAsync(async () =>
        {
            var result = await notificationService.GetAllChatSettingsAsync(userId);
            return Result<List<ChatNotificationSettingsDto>>.Success(result);
        }, "Настройки получены");
    }
}