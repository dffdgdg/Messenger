using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Api;
using MessengerShared.DTO.Chat;
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
        var result = await apiClient.GetAsync<ChatNotificationSettingsDTO>(ApiEndpoints.Notification.ChatSettings(chatId), ct);

        return result.Success ? result.Data : null;
    }

    public async Task<bool> SetChatMuteAsync(int chatId, bool isMuted, CancellationToken ct = default)
    {
        var result = await apiClient.PostAsync<ChatNotificationSettingsDTO, ChatNotificationSettingsDTO>(ApiEndpoints.Notification.SetMute,
            new ChatNotificationSettingsDTO { ChatId = chatId, NotificationsEnabled = isMuted }, ct);
        return result.Success;
    }

    public async Task<List<ChatNotificationSettingsDTO>> GetAllSettingsAsync(CancellationToken ct = default)
    {
        var result = await apiClient.GetAsync<List<ChatNotificationSettingsDTO>>(ApiEndpoints.Notification.AllSettings, ct);

        return result.Success && result.Data != null ? result.Data : [];
    }
}