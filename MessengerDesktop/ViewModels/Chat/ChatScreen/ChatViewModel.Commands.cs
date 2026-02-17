using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.ViewModels.Chat.Managers;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    /// <summary>Удалить локальное вложение из списка перед отправкой.</summary>
    [RelayCommand]
    private void RemoveAttachment(LocalFileAttachment attachment) => _attachmentManager.Remove(attachment);

    /// <summary>Вставить эмодзи в текст сообщения.</summary>
    [RelayCommand]
    private void InsertEmoji(string emoji) => NewMessage += emoji;

    /// <summary>Открыть диалог выбора файлов и добавить вложения.</summary>
    [RelayCommand]
    private async Task AttachFile()
    {
        if (!await _attachmentManager.PickAndAddFilesAsync())
        {
            ErrorMessage = "Не удалось выбрать файлы";
        }
    }

    /// <summary>Переключить боковую информационную панель.</summary>
    [RelayCommand]
    private void ToggleInfoPanel() => IsInfoPanelOpen = !IsInfoPanelOpen;

    /// <summary>Покинуть текущий чат.</summary>
    [RelayCommand]
    private async Task LeaveChat()
    {
        await SafeExecuteAsync(async ct =>
        {
            var result = await _apiClient.PostAsync(ApiEndpoints.Chat.Leave(_chatId, UserId), null, ct);

            if (result.Success)
                SuccessMessage = "Вы покинули чат";
            else
                ErrorMessage = $"Ошибка при выходе из чата: {result.Error}";
        });
    }

    /// <summary>Открыть диалог создания опроса (доступно только в групповых чатах для админов).</summary>
    [RelayCommand]
    private async Task OpenCreatePoll()
    {
        if (Parent?.Parent is MainMenuViewModel menu)
        {
            await menu.ShowPollDialogAsync(
                _chatId,
                async () => await _messageManager.LoadInitialMessagesAsync());
            return;
        }

        await _notificationService.ShowErrorAsync("Не удалось открыть диалог опроса", copyToClipboard: false);
    }

    /// <summary>Открыть профиль пользователя по ID.</summary>
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

    /// <summary>
    /// Переключить уведомления для текущего чата.
    /// Защищено от повторных нажатий через <see cref="IsLoadingMuteState"/>.
    /// </summary>
    [RelayCommand]
    private async Task ToggleChatNotifications()
    {
        if (IsLoadingMuteState) return;

        try
        {
            IsLoadingMuteState = true;
            var desiredState = !IsNotificationEnabled;

            var success = await _notificationApiService.SetChatMuteAsync(_chatId, desiredState);

            if (success)
            {
                IsNotificationEnabled = desiredState;
                var message = desiredState
                    ? "Уведомления включены для этого чата"
                    : "Уведомления отключены для этого чата";
                await _notificationService.ShowInfoAsync(message);
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