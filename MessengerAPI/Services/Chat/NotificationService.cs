using MessengerAPI.Model;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO.Chat;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.Notification;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services.Chat
{
    public interface INotificationService
    {
        Task SendNotificationAsync(int userId, MessageDTO message);
        Task<ChatNotificationSettingsDTO?> GetChatNotificationSettingsAsync(int userId, int chatId);
        Task<ChatNotificationSettingsDTO> SetChatMuteAsync(int userId, ChatNotificationSettingsDTO request);
        Task<List<ChatNotificationSettingsDTO>> GetAllChatSettingsAsync(int userId);
    }

    public class NotificationService(MessengerDbContext context, IHubNotifier hubNotifier, IUrlBuilder urlBuilder,
        ILogger<NotificationService> logger) : INotificationService
    {
        public async Task SendNotificationAsync(int userId, MessageDTO message)
        {
            try
            {
                var notification = await BuildNotificationAsync(message);
                await hubNotifier.SendToUserAsync(userId, "ReceiveNotification", notification);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,"Не удалось отправить уведомление пользователю {UserId}",userId);
            }
        }

        public async Task<ChatNotificationSettingsDTO?> GetChatNotificationSettingsAsync(int userId, int chatId)
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

            logger.LogInformation("Пользователь {UserId} {Action} уведомления для чата {ChatId}",
                userId, request.NotificationsEnabled ? "включил" : "отключил", request.ChatId);

            return new ChatNotificationSettingsDTO
            {
                ChatId = request.ChatId,
                NotificationsEnabled = member.NotificationsEnabled
            };
        }

        public async Task<List<ChatNotificationSettingsDTO>> GetAllChatSettingsAsync(int userId)
            => await context.ChatMembers.Where(cm => cm.UserId == userId)
            .Select(cm => new ChatNotificationSettingsDTO
            {
                ChatId = cm.ChatId,
                NotificationsEnabled = cm.NotificationsEnabled
            })
            .AsNoTracking().ToListAsync();

        #region Private Methods

        private async Task<NotificationDTO> BuildNotificationAsync(MessageDTO message)
        {
            var chat = await context.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == message.ChatId);

            return new NotificationDTO
            {
                Type = message.Poll != null ? "poll" : "message",
                ChatId = message.ChatId,
                ChatName = chat?.Type == ChatType.Contact
                    ? message.SenderName
                    : chat?.Name,
                ChatAvatar = chat?.Type == ChatType.Contact
                    ? message.SenderAvatarUrl
                    : urlBuilder.BuildUrl(chat?.Avatar),
                MessageId = message.Id,
                SenderId = message.SenderId,
                SenderName = message.SenderName,
                SenderAvatar = message.SenderAvatarUrl,
                Preview = message.Poll != null
                    ? $"📊 {message.Content}"
                    : TruncateText(message.Content, 100),
                CreatedAt = message.CreatedAt
            };
        }

        private static string? TruncateText(string? content, int maxLength)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            return content.Length <= maxLength
                ? content
                : content[..maxLength] + "...";
        }

        #endregion
    }
}