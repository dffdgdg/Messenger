using MessengerAPI.Services.Base;

namespace MessengerAPI.Services.Chat;

public interface ISystemMessageService
{
    Task CreateAsync(int chatId, int senderId, SystemEventType eventType, int? targetUserId = null, string? content = null);
    Task CreateCallEndedMessageAsync(int chatId, int initiatorId, TimeSpan duration);
    Task CreateCallStartedMessageAsync(int chatId, int initiatorId);

}

public sealed class SystemMessageService(MessengerDbContext context,IHubNotifier hubNotifier,IUrlBuilder urlBuilder,AppDateTime appDateTime, ILogger<SystemMessageService> logger)
    : BaseService<SystemMessageService>(context, logger), ISystemMessageService
{
    public async Task CreateAsync(int chatId, int senderId, SystemEventType eventType, int? targetUserId = null, string? content = null)
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
                Content = content,
                CreatedAt = appDateTime.UtcNow,
                IsDeleted = false,
                IsSystemMessage = true,
                SystemEventType = eventType,
                TargetUserId = targetUserId
            };

            _context.Messages.Add(message);
            chat.LastMessageTime = appDateTime.UtcNow;
            await _context.SaveChangesAsync();

            var loaded = await _context.Messages.Include(m => m.Sender).Include(m => m.TargetUser).AsNoTracking()
                .FirstAsync(m => m.Id == message.Id);

            await hubNotifier.SendToChatAsync(chatId, "ReceiveMessageDto", loaded.ToDto(urlBuilder: urlBuilder));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка создания системного сообщения [{EventType}] в чате {ChatId}", eventType, chatId);
        }
    }

    public async Task CreateCallEndedMessageAsync(int chatId, int initiatorId, TimeSpan duration)
    {
        var durationText = duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";

        await CreateAsync(chatId, initiatorId, SystemEventType.CallEnded, content: $"Звонок завершён · {durationText}");
    }
    public async Task CreateCallStartedMessageAsync(int chatId, int initiatorId)
        => await CreateAsync(chatId, initiatorId, SystemEventType.CallStarted, content: null);
}