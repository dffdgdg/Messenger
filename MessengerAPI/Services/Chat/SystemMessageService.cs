using MessengerAPI.Services.Base;

namespace MessengerAPI.Services.Chat;

public interface ISystemMessageService
{
    Task CreateAsync(int chatId, int senderId, SystemEventType eventType, int? targetUserId = null);
}

public sealed class SystemMessageService(MessengerDbContext context,IHubNotifier hubNotifier,IUrlBuilder urlBuilder,AppDateTime appDateTime,
    ILogger<SystemMessageService> logger) : BaseService<SystemMessageService>(context, logger), ISystemMessageService
{
    public async Task CreateAsync(int chatId, int senderId, SystemEventType eventType, int? targetUserId = null)
    {
        try
        {
            var chat = await _context.Chats.FindAsync(chatId);
            if (chat is null || chat.Type == ChatType.Contact)
                return;

            var message = new Message
            {
                ChatId = chatId,
                SenderId = senderId,
                Content = null,
                CreatedAt = appDateTime.UtcNow,
                IsDeleted = false,
                IsSystemMessage = true,
                SystemEventType = eventType,
                TargetUserId = targetUserId
            };

            _context.Messages.Add(message);
            chat.LastMessageTime = appDateTime.UtcNow;
            await _context.SaveChangesAsync();

            var loaded = await _context.Messages.Include(m => m.Sender).Include(m => m.TargetUser).AsNoTracking().FirstAsync(m => m.Id == message.Id);

            var dto = loaded.ToDto(urlBuilder: urlBuilder);
            await hubNotifier.SendToChatAsync(chatId, "ReceiveMessageDto", dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка создания системного сообщения [{EventType}] в чате {ChatId}", eventType, chatId);
        }
    }
}