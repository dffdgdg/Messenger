using MessengerDesktop.Services.Api;
using MessengerShared.DTO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services;

public interface IChatNotificationApiService
{
    Task<ChatNotificationSettingsDTO?> GetChatSettingsAsync(int chatId, CancellationToken ct = default);
    Task<bool> SetChatMuteAsync(int chatId, bool isMuted, CancellationToken ct = default);
    Task<List<ChatNotificationSettingsDTO>> GetAllSettingsAsync(CancellationToken ct = default);
}

public class ChatNotificationApiService(IApiClientService apiClient) : IChatNotificationApiService
{
    public async Task<ChatNotificationSettingsDTO?> GetChatSettingsAsync(int chatId, CancellationToken ct = default)
    {
        var result = await apiClient.GetAsync<ChatNotificationSettingsDTO>(
            $"api/notification/chat/{chatId}/settings", ct);

        return result.Success ? result.Data : null;
    }

    public async Task<bool> SetChatMuteAsync(int chatId, bool NotificationsEnabled, CancellationToken ct = default)
    {
        var result = await apiClient.PostAsync<ChatNotificationSettingsDTO, ChatNotificationSettingsDTO>("api/notification/chat/mute",
            new ChatNotificationSettingsDTO { ChatId = chatId, NotificationsEnabled = NotificationsEnabled }, ct);
        return result.Success;
    }

    public async Task<List<ChatNotificationSettingsDTO>> GetAllSettingsAsync(CancellationToken ct = default)
    {
        var result = await apiClient.GetAsync<List<ChatNotificationSettingsDTO>>("api/notification/settings", ct);
        return result.Success && result.Data != null ? result.Data : [];
    }
}