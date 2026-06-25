using API.Application.Features.Notification;
using API.Application.Features.Notification.Commands;
using API.Application.Features.Notification.Queries;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Chat;

namespace API.Web.Controllers;

public sealed class NotificationsController(INotificationHandlers handlers, ILogger<NotificationsController> logger)
    : BaseController<NotificationsController>(logger)
{
    [HttpGet("chat/{chatId}/settings")]
    public async Task<IActionResult> GetChatSettings(int chatId)
        => Map(await handlers.GetChatSettings.HandleAsync(new GetChatNotificationSettingsQuery(GetCurrentUserId(), chatId)));

    [HttpPost("chat/mute")]
    public async Task<IActionResult> SetChatMute([FromBody] ChatNotificationSettingsDto request)
        => Map(await handlers.SetChatMute.HandleAsync(new SetChatMuteCommand(GetCurrentUserId(), request)));

    [HttpGet("settings")]
    public async Task<IActionResult> GetAllSettings()
        => Map(await handlers.GetAllSettings.HandleAsync(new GetAllChatSettingsQuery(GetCurrentUserId())));
}