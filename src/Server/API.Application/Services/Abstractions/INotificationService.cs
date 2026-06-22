using API.Domain.Common;
using Shared.Dto.Chat;
using Shared.Dto.Message;

namespace API.Application.Services.Abstractions;

public interface INotificationService
{
    Task SendNotificationAsync(int userId, MessageDto message);
    Task SendMentionNotificationAsync(int userId, MessageDto message);
    Task<Result<ChatNotificationSettingsDto>> GetChatNotificationSettingsAsync(int userId, int chatId);
    Task<Result<ChatNotificationSettingsDto>> SetChatMuteAsync(int userId, ChatNotificationSettingsDto request);
    Task<Result<List<ChatNotificationSettingsDto>>> GetAllChatSettingsAsync(int userId);
}