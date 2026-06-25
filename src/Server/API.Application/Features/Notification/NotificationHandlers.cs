using API.Application.Common;
using API.Application.Features.Notification.Commands;
using API.Application.Features.Notification.Queries;
using API.Domain.Common;
using Shared.Contracts.Chat;

namespace API.Application.Features.Notification;

public class NotificationHandlers(
    SetChatMuteCommandHandler setChatMute,
    GetChatNotificationSettingsQueryHandler getChatSettings,
    GetAllChatSettingsQueryHandler getAllSettings)
    : INotificationHandlers
{
    public ICommandHandler<SetChatMuteCommand, ChatNotificationSettingsDto> SetChatMute => setChatMute;
    public IQueryHandler<GetChatNotificationSettingsQuery, Result<ChatNotificationSettingsDto>> GetChatSettings => getChatSettings;
    public IQueryHandler<GetAllChatSettingsQuery, Result<List<ChatNotificationSettingsDto>>> GetAllSettings => getAllSettings;
}