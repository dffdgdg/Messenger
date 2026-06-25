using Shared.Contracts.Chat;

namespace API.Application.Features.Notification.Commands;

public sealed record SetChatMuteCommand(int UserId, ChatNotificationSettingsDto Request);
