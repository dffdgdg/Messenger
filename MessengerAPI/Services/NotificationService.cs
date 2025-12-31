using MessengerAPI.Helpers;
using MessengerAPI.Hubs;
using MessengerAPI.Model;
using MessengerShared.DTO;
using MessengerShared.Enum;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface INotificationService
    {
        Task NotifyNewMessageAsync(MessageDTO message, HttpRequest request);
        Task<ChatNotificationSettingsDTO?> GetChatNotificationSettingsAsync(int userId, int chatId);
        Task<ChatNotificationSettingsDTO> SetChatMuteAsync(int userId, ChatNotificationSettingsDTO request);
        Task<List<ChatNotificationSettingsDTO>> GetAllChatSettingsAsync(int userId);
    }

    public class NotificationService(MessengerDbContext context,IHubContext<ChatHub> hubContext,IServiceScopeFactory scopeFactory,
        ILogger<NotificationService> logger) : INotificationService
    {
        public async Task NotifyNewMessageAsync(MessageDTO message, HttpRequest request)
        {
            try
            {
                var usersToNotify = await GetUsersToNotifyAsync(message.ChatId, message.SenderId);

                if (usersToNotify.Count == 0)
                {
                    logger.LogDebug("Нет пользователей для уведомления в чате {ChatId}", message.ChatId);
                    return;
                }

                var notification = await BuildNotificationAsync(message, request);

                foreach (var userId in usersToNotify)
                {
                    await SendNotificationToUserAsync(userId, notification, message.ChatId);
                }

                logger.LogDebug(
                    "Уведомления отправлены {Count} пользователям о сообщении {MessageId}",
                    usersToNotify.Count, message.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка отправки уведомлений о сообщении {MessageId}", message.Id);
            }
        }

        public async Task<ChatNotificationSettingsDTO?> GetChatSettingsAsync(int userId, int chatId)
        {
            var member = await context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ChatId == chatId);

            if (member == null)
                return null;

            return new ChatNotificationSettingsDTO
            {
                ChatId = chatId,
                NotificationsEnabled = member.NotificationsEnabled
            };
        }

        public async Task<ChatNotificationSettingsDTO> SetChatMuteAsync(int userId, ChatNotificationSettingsDTO request)
        {
            var member = await context.ChatMembers.FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ChatId == request.ChatId)
                ?? throw new KeyNotFoundException($"Пользователь не является участником чата {request.ChatId}");

            member.NotificationsEnabled = request.NotificationsEnabled;
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Пользователь {UserId} {Action} уведомления для чата {ChatId}",
                userId,
                request.NotificationsEnabled ? "включил" : "отключил",
                request.ChatId);

            return new ChatNotificationSettingsDTO
            {
                ChatId = request.ChatId,
                NotificationsEnabled = member.NotificationsEnabled
            };
        }

        public async Task<List<ChatNotificationSettingsDTO>> GetAllChatSettingsAsync(int userId)
        {
            return await context.ChatMembers.Where(cm => cm.UserId == userId).Select(cm => new ChatNotificationSettingsDTO
            {
                ChatId = cm.ChatId,
                NotificationsEnabled = cm.NotificationsEnabled
            }).AsNoTracking().ToListAsync();
        }

        #region Private Methods

        private async Task<List<int>> GetUsersToNotifyAsync(int chatId, int excludeUserId) => await context.ChatMembers
            .Where(cm => cm.ChatId == chatId && cm.UserId != excludeUserId).Where(cm => cm.NotificationsEnabled)
            .Where(cm => cm.User.UserSetting == null || cm.User.UserSetting.NotificationsEnabled)
            .Select(cm => cm.UserId).ToListAsync();

        private async Task<NotificationDTO> BuildNotificationAsync(MessageDTO message, HttpRequest request)
        {
            var chat = await context.Chats
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == message.ChatId);

            return new NotificationDTO
            {
                Type = message.Poll != null ? "poll" : "message",
                ChatId = message.ChatId,
                ChatName = chat?.Type == ChatType.Contact ? message.SenderName : chat?.Name,
                ChatAvatar = chat?.Type == ChatType.Contact
                    ? message.SenderAvatarUrl
                    : (chat?.Avatar).BuildFullUrl(request),
                MessageId = message.Id,
                SenderId = message.SenderId,
                SenderName = message.SenderName,
                SenderAvatar = message.SenderAvatarUrl,
                Preview = message.Poll != null
                    ? $"📊 {message.Poll.Question}"
                    : TruncateText(message.Content, 100),
                CreatedAt = message.CreatedAt
            };
        }

        private async Task SendNotificationToUserAsync(int userId, NotificationDTO notification, int chatId)
        {
            try
            {
                var userGroup = $"user_{userId}";

                await hubContext.Clients.Group(userGroup).SendAsync("ReceiveNotification", notification);

                // Обновляем счётчик непрочитанных
                using var scope = scopeFactory.CreateScope();
                var readReceiptService = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();
                var unreadCount = await readReceiptService.GetUnreadCountAsync(userId, chatId);

                await hubContext.Clients.Group(userGroup).SendAsync("UnreadCountUpdated", new
                {
                    ChatId = chatId,
                    UnreadCount = unreadCount
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось отправить уведомление пользователю {UserId}", userId);
            }
        }
        public async Task<ChatNotificationSettingsDTO?> GetChatNotificationSettingsAsync(int userId, int chatId)
        {
            var member = await context.ChatMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ChatId == chatId);

            if (member == null)
                return null;

            return new ChatNotificationSettingsDTO
            {
                ChatId = chatId,
                NotificationsEnabled = member.NotificationsEnabled
            };
        }
        private static string? TruncateText(string? content, int maxLength)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            return content.Length <= maxLength ? content : content[..maxLength] + "...";
        }

        #endregion
    }
}