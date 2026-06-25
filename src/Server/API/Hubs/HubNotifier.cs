using API.Application.Services.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace API.Web.Hubs;

public sealed class HubNotifier(IHubContext<MessengerHub> hubContext, IOnlineUserService onlineUserService, ILogger<HubNotifier> logger)
    : IHubNotifier
{
    public async Task SendToChatAsync(int chatId, string method, params object?[] args)
    {
        try
        {
            await hubContext.Clients.Group($"chat_{chatId}").SendCoreAsync(method, args!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось отправить {Method} в чат {ChatId}", method, chatId);
        }
    }

    public async Task SendToUserAsync(int userId, string method, params object?[] args)
    {
        try
        {
            await hubContext.Clients.Group($"user_{userId}").SendCoreAsync(method, args!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось отправить {Method} пользователю {UserId}", method, userId);
        }
    }

    public async Task AddUserToGroupAsync(int userId, string groupName)
    {
        foreach (var connectionId in onlineUserService.GetConnectionIds(userId))
            await hubContext.Groups.AddToGroupAsync(connectionId, groupName);
    }

    public async Task RemoveUserFromGroupAsync(int userId, string groupName)
    {
        foreach (var connectionId in onlineUserService.GetConnectionIds(userId))
            await hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName);
    }
}