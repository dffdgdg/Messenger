using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Abstractions;

public interface IChatNotificationApiService
{
    Task<ChatNotificationSettingsDto?> GetChatSettingsAsync(int chatId, CancellationToken ct = default);
    Task<bool> SetChatMuteAsync(int chatId, bool isMuted, CancellationToken ct = default);
    Task<List<ChatNotificationSettingsDto>> GetAllSettingsAsync(CancellationToken ct = default);
}