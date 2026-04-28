using MessengerAPI.Services.Base;

namespace MessengerAPI.Services.Chat;

public interface INotificationService
{
    Task SendNotificationAsync(int userId, MessageDto message);
    Task SendMentionNotificationAsync(int userId, MessageDto message);
    Task<Result<ChatNotificationSettingsDto>> GetChatNotificationSettingsAsync(int userId, int chatId);
    Task<Result<ChatNotificationSettingsDto>> SetChatMuteAsync(int userId, ChatNotificationSettingsDto request);
    Task<Result<List<ChatNotificationSettingsDto>>> GetAllChatSettingsAsync(int userId);
}
public sealed partial class NotificationService(MessengerDbContext context, IHubNotifier hubNotifier, IUrlBuilder urlBuilder, ILogger<NotificationService> logger)
    : BaseService<NotificationService>(context, logger), INotificationService
{
    private readonly IHubNotifier _hubNotifier = hubNotifier;
    private readonly IUrlBuilder _urlBuilder = urlBuilder;

    public Task SendNotificationAsync(int userId, MessageDto message) => SendNotificationInternalAsync(userId, message, "message");

    public Task SendMentionNotificationAsync(int userId, MessageDto message) => SendNotificationInternalAsync(userId, message, "mention");

    public async Task<Result<ChatNotificationSettingsDto>> GetChatNotificationSettingsAsync(int userId, int chatId)
    {
        var member = await GetMemberAsync(userId, chatId);

        if (member is null)
            return Result<ChatNotificationSettingsDto>.Failure($"Пользователь не является участником чата {chatId}");

        return Result<ChatNotificationSettingsDto>.Success(new ChatNotificationSettingsDto
        {
            ChatId = chatId,
            NotificationsEnabled = member.NotificationsEnabled
        });
    }

    public async Task<Result<ChatNotificationSettingsDto>> SetChatMuteAsync(int userId, ChatNotificationSettingsDto request)
    {
        var member = await GetMemberAsync(userId, request.ChatId, tracked: true);

        if (member is null)
            return Result<ChatNotificationSettingsDto>.Failure($"Пользователь не является участником чата {request.ChatId}");

        member.NotificationsEnabled = request.NotificationsEnabled;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<ChatNotificationSettingsDto>();

        LogNotificationSettingsChanged(userId, request.NotificationsEnabled ? "включил" : "отключил", request.ChatId);

        return Result<ChatNotificationSettingsDto>.Success(new ChatNotificationSettingsDto
        {
            ChatId = request.ChatId,
            NotificationsEnabled = member.NotificationsEnabled
        });
    }

    public async Task<Result<List<ChatNotificationSettingsDto>>> GetAllChatSettingsAsync(int userId)
    {
        var settings = await _context.ChatMembers.Where(cm => cm.UserId == userId).AsNoTracking().Select(cm => new ChatNotificationSettingsDto
        {
            ChatId = cm.ChatId,
            NotificationsEnabled = cm.NotificationsEnabled
        }).ToListAsync();

        return Result<List<ChatNotificationSettingsDto>>.Success(settings);
    }

    private async Task SendNotificationInternalAsync(int userId, MessageDto message, string type)
    {
        try
        {
            var notification = await BuildNotificationAsync(message, type);
            await _hubNotifier.SendToUserAsync(userId, "ReceiveNotification", notification);
        }
        catch (Exception ex)
        {
            LogNotificationFailed(userId, ex);
        }
    }

    private async Task<ChatMember?> GetMemberAsync(int userId, int chatId, bool tracked = false)
    {
        var query = _context.ChatMembers.Where(cm => cm.UserId == userId && cm.ChatId == chatId);
        return tracked ? await query.FirstOrDefaultAsync() : await query.AsNoTracking().FirstOrDefaultAsync();
    }

    private async Task<NotificationDto> BuildNotificationAsync(MessageDto message, string type)
    {
        var chat = await _context.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == message.ChatId);
        var isContact = chat?.Type == ChatType.Contact;

        return new NotificationDto
        {
            Type = ResolveNotificationType(type, message),
            ChatId = message.ChatId,
            ChatName = isContact ? message.SenderName : chat?.Name,
            ChatAvatar = isContact ? message.SenderAvatarUrl : _urlBuilder.BuildUrl(chat?.Avatar),
            MessageId = message.Id,
            SenderId = message.SenderId,
            SenderName = message.SenderName,
            SenderAvatar = message.SenderAvatarUrl,
            Preview = ResolvePreview(type, message),
            CreatedAt = message.CreatedAt
        };
    }

    private static string ResolveNotificationType(string type, MessageDto message) => type switch
    {
        "mention" => "mention", _ => message.Poll != null ? "poll" : "message"
    };

    private static string? ResolvePreview(string type, MessageDto message) => type switch
    {
        "mention" => $"Вас упомянули: {TruncateText(message.Content, 100)}",
        _ => TruncateText(message.Content, 100)
    };

    private static string? TruncateText(string? content, int maxLength)
        => string.IsNullOrEmpty(content) ? null : content.Length <= maxLength ? content : content[..maxLength] + "...";

    #region Logging

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось отправить уведомление пользователю {UserId}")]
    private partial void LogNotificationFailed(int userId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} {Action} уведомления для чата {ChatId}", EventName = "ChatNotificationSettingsChanged")]
    private partial void LogNotificationSettingsChanged(int userId, string action, int chatId);

    #endregion
}