using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services;

public interface IChatNotificationApiService
{
    Task<ChatNotificationSettingsDto?> GetChatSettingsAsync(int chatId, CancellationToken ct = default);
    Task<bool> SetChatMuteAsync(int chatId, bool isMuted, CancellationToken ct = default);
    Task<List<ChatNotificationSettingsDto>> GetAllSettingsAsync(CancellationToken ct = default);
}

public class ChatNotificationApiService(IApiClientService apiClient) : IChatNotificationApiService
{
    public async Task<ChatNotificationSettingsDto?> GetChatSettingsAsync(int chatId, CancellationToken ct = default)
    {
        var result = await apiClient.GetAsync<ChatNotificationSettingsDto>(ApiEndpoints.Notification.ChatSettings(chatId), ct);

        return result.Success ? result.Data : null;
    }

    public async Task<bool> SetChatMuteAsync(int chatId, bool isMuted, CancellationToken ct = default)
    {
        var result = await apiClient.PostAsync<ChatNotificationSettingsDto, ChatNotificationSettingsDto>(ApiEndpoints.Notification.SetMute,
            new ChatNotificationSettingsDto { ChatId = chatId, NotificationsEnabled = isMuted }, ct);
        return result.Success;
    }

    public async Task<List<ChatNotificationSettingsDto>> GetAllSettingsAsync(CancellationToken ct = default)
    {
        var result = await apiClient.GetAsync<List<ChatNotificationSettingsDto>>(ApiEndpoints.Notification.AllSettings, ct);

        return result.Success && result.Data != null ? result.Data : [];
    }
}