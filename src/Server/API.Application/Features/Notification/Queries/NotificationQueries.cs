namespace API.Application.Features.Notification.Queries;

public sealed record GetChatNotificationSettingsQuery(int UserId, int ChatId);
public sealed record GetAllChatSettingsQuery(int UserId);
