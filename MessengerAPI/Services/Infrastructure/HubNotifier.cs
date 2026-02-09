using MessengerAPI.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MessengerAPI.Services.Infrastructure
{
    public interface IHubNotifier
    {
        Task SendToChatAsync<T>(int chatId, string method, T data);
        Task SendToUserAsync<T>(int userId, string method, T data);
    }

    public class HubNotifier(IHubContext<ChatHub> hubContext, ILogger<HubNotifier> logger) : IHubNotifier
    {
        public async Task SendToChatAsync<T>(int chatId, string method, T data)
        {
            try
            {
                await hubContext.Clients
                    .Group($"chat_{chatId}")
                    .SendAsync(method, data);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Не удалось отправить {Method} в чат {ChatId}", method, chatId);
            }
        }

        public async Task SendToUserAsync<T>(int userId, string method, T data)
        {
            try
            {
                await hubContext.Clients
                    .Group($"user_{userId}")
                    .SendAsync(method, data);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Не удалось отправить {Method} пользователю {UserId}", method, userId);
            }
        }
    }
}