using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using API.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Shared.Dto.Chat;
using Shared.Dto.Message;
using Shared.Dto.Notification;
using Shared.Enum;
using Shared.Hubs;

namespace API.Application.Services.Features.Chat;

public sealed partial class NotificationService(
    IUnitOfWork unitOfWork,
    IChatRepository chatRepository,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder,
    ILogger<NotificationService> logger)
    : BaseService<NotificationService>(unitOfWork, logger), INotificationService
{
    private readonly IChatRepository _chatRepository = chatRepository;
    private readonly IHubNotifier _hubNotifier = hubNotifier;
    private readonly IUrlBuilder _urlBuilder = urlBuilder;

    public Task SendNotificationAsync(int userId, MessageDto message)
        => SendNotificationInternalAsync(userId, message, "message");

    public Task SendMentionNotificationAsync(int userId, MessageDto message)
        => SendNotificationInternalAsync(userId, message, "mention");

    public async Task<Result<ChatNotificationSettingsDto>> GetChatNotificationSettingsAsync(int userId, int chatId)
    {
        var member = await _chatRepository.GetMemberAsync(chatId, userId);

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
        var member = await _chatRepository.GetMemberAsync(request.ChatId, userId);

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
        var memberIds = await _chatRepository.GetMemberIdsForUserAsync(userId);

        if (memberIds.Count == 0)
            return Result<List<ChatNotificationSettingsDto>>.Success([]);

        var result = new List<ChatNotificationSettingsDto>(memberIds.Count);
        foreach (var chatId in memberIds)
        {
            var member = await _chatRepository.GetMemberAsync(chatId, userId);
            if (member is not null)
            {
                result.Add(new ChatNotificationSettingsDto
                {
                    ChatId = chatId,
                    NotificationsEnabled = member.NotificationsEnabled
                });
            }
        }

        return Result<List<ChatNotificationSettingsDto>>.Success(result);
    }

    private async Task SendNotificationInternalAsync(int userId, MessageDto message, string type)
    {
        try
        {
            var notification = await BuildNotificationAsync(message, type);
            await _hubNotifier.SendToUserAsync(userId, HubMethods.Chat.ReceiveNotification, notification);
        }
        catch (Exception ex)
        {
            LogNotificationFailed(userId, ex);
        }
    }

    private async Task<NotificationDto> BuildNotificationAsync(MessageDto message, string type)
    {
        var chat = await _chatRepository.FindByIdAsync(message.ChatId);
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
        "mention" => "mention",
        _ => message.Poll != null ? "poll" : "message"
    };

    private static string? ResolvePreview(string type, MessageDto message) => type switch
    {
        "mention" => $"Вас упомянули: {TruncateText(message.Content, 100)}",
        _ => TruncateText(message.Content, 100)
    };

    private static string? TruncateText(string? content, int maxLength)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }
        else if (content.Length <= maxLength)
        {
            return content;
        }
        else
        {
            return content[..maxLength] + "...";
        }
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось отправить уведомление пользователю {UserId}")]
    private partial void LogNotificationFailed(int userId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} {Action} уведомления для чата {ChatId}", EventName = "ChatNotificationSettingsChanged")]
    private partial void LogNotificationSettingsChanged(int userId, string action, int chatId);

    #endregion
}