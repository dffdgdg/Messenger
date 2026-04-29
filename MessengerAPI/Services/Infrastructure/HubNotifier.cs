using MessengerAPI.Hubs;

namespace MessengerAPI.Services.Infrastructure;

public class HubNotifier(IHubContext<ChatHub> hubContext, ILogger<HubNotifier> logger) : IHubNotifier
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
}