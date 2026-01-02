using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.ViewModels.Chat.Managers;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    [RelayCommand]
    private void RemoveAttachment(LocalFileAttachment attachment) => _attachmentManager.Remove(attachment);

    [RelayCommand]
    private void InsertEmoji(string emoji) => NewMessage += emoji;

    [RelayCommand]
    private async Task AttachFile()
    {
        if (!await _attachmentManager.PickAndAddFilesAsync())
        {
            ErrorMessage = "Не удалось выбрать файлы";
        }
    }

    [RelayCommand]
    private void ToggleInfoPanel() => IsInfoPanelOpen = !IsInfoPanelOpen;

    [RelayCommand]
    private async Task LeaveChat() =>
        await SafeExecuteAsync(async ct =>
        {
            var result = await _apiClient.PostAsync(ApiEndpoints.Chat.Leave(_chatId, UserId), null, ct);

            if (result.Success) SuccessMessage = "Вы покинули чат";
            else                ErrorMessage = $"Ошибка при выходе из чата: {result.Error}";
        });

    [RelayCommand]
    private async Task OpenCreatePoll()
    {
        if (Parent?.Parent is MainMenuViewModel menu)
        {
            await menu.ShowPollDialogAsync(_chatId, async () => await _messageManager.LoadInitialMessagesAsync());
            return;
        }

        await _notificationService.ShowErrorAsync("Не удалось открыть диалог опроса", copyToClipboard: false);
    }

    [RelayCommand]
    public async Task OpenProfile(int userId)
    {
        if (Parent?.Parent is MainMenuViewModel menu)
        {
            await menu.ShowUserProfileAsync(userId);
            return;
        }

        await _notificationService.ShowErrorAsync("Не удалось открыть профиль", copyToClipboard: false);
    }

    [RelayCommand]
    private async Task ToggleChatNotifications()
    {
        if (IsLoadingMuteState) return;

        try
        {
            IsLoadingMuteState = true;
            var newNotificationsState = !IsNotificationEnabled;

            var success = await _notificationApiService.SetChatMuteAsync(_chatId, newNotificationsState);

            if (success)
            {
                IsNotificationEnabled = newNotificationsState;
                await _notificationService.ShowInfoAsync(
                    newNotificationsState ? "Уведомления включены для этого чата" : "Уведомления отключены для этого чата");
            }
            else
            {
                await _notificationService.ShowErrorAsync("Не удалось изменить настройки");
            }
        }
        catch (Exception ex)
        {
            await _notificationService.ShowErrorAsync($"Ошибка: {ex.Message}");
        }
        finally
        {
            IsLoadingMuteState = false;
        }
    }
}