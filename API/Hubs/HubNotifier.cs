using API.Application.Services.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace API.Web.Hubs;

public class HubNotifier(IHubContext<MessengerHub> hubContext, ILogger<HubNotifier> logger, IOnlineUserService onlineUserService) : IHubNotifier
{
    public async Task AddUserToChatGroupAsync(int userId, int chatId)
    {
        var groupName = $"chat_{chatId}";
        foreach (var connectionId in onlineUserService.GetConnectionIds(userId))
            await hubContext.Groups.AddToGroupAsync(connectionId, groupName);
    }

    public async Task RemoveUserFromChatGroupAsync(int userId, int chatId)
    {
        var groupName = $"chat_{chatId}";
        foreach (var connectionId in onlineUserService.GetConnectionIds(userId))
            await hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName);
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
    public async Task SendToUserConnectionAsync(string userId, string method, params object?[] args)
    {
        try
        {
            await hubContext.Clients.User(userId).SendCoreAsync(method, args!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось отправить {Method} пользователю {UserId}", method, userId);
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
}