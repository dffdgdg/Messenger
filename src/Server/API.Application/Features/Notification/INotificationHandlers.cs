using API.Application.Common;
using API.Application.Features.Notification.Commands;
using API.Application.Features.Notification.Queries;
using API.Domain.Common;
using Shared.Contracts.Chat;

namespace API.Application.Features.Notification;

public interface INotificationHandlers
{
    ICommandHandler<SetChatMuteCommand, ChatNotificationSettingsDto> SetChatMute { get; }
    IQueryHandler<GetChatNotificationSettingsQuery, Result<ChatNotificationSettingsDto>> GetChatSettings { get; }
    IQueryHandler<GetAllChatSettingsQuery, Result<List<ChatNotificationSettingsDto>>> GetAllSettings { get; }
}