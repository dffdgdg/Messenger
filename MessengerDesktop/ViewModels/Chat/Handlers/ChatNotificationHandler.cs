using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatNotificationHandler(ChatContext context) : ChatFeatureHandler(context)
{
    [ObservableProperty] private bool _isNotificationEnabled;
    [ObservableProperty] private bool _isLoadingMuteState;

    public async Task LoadSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = await Ctx.NotificationApi
                .GetChatSettingsAsync(Ctx.ChatId, ct);
            if (settings != null)
                IsNotificationEnabled = settings.NotificationsEnabled;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Notification] LoadSettings error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Toggle()
    {
        if (IsLoadingMuteState) return;

        try
        {
            IsLoadingMuteState = true;
            var desired = !IsNotificationEnabled;
            var success = await Ctx.NotificationApi.SetChatMuteAsync(Ctx.ChatId, desired);

            if (success)
            {
                IsNotificationEnabled = desired;
                await Ctx.Notifications.ShowInfoAsync(desired ? "Уведомления включены для этого чата"
                    : "Уведомления отключены для этого чата");
            }
            else
            {
                await Ctx.Notifications.ShowErrorAsync("Не удалось изменить настройки");
            }
        }
        catch (Exception ex)
        {
            await Ctx.Notifications.ShowErrorAsync($"Ошибка: {ex.Message}");
        }
        finally
        {
            IsLoadingMuteState = false;
        }
    }
}
