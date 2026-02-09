using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerShared.DTO.Message;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    private MessageViewModel? _editingMessage;

    [ObservableProperty]
    private string _editMessageContent = string.Empty;

    public bool IsEditMode => EditingMessage != null;

    [RelayCommand]
    private void StartEditMessage(MessageViewModel? message)
    {
        if (message?.CanEdit != true) return;

        CancelEditMessage();
        EditingMessage = message;
        EditMessageContent = message.Content ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveEditMessage()
    {
        if (EditingMessage == null) return;

        var newContent = EditMessageContent?.Trim();

        if (newContent == EditingMessage.Content)
        {
            CancelEditMessage();
            return;
        }

        if (string.IsNullOrWhiteSpace(newContent))
        {
            await DeleteMessage(EditingMessage);
            CancelEditMessage();
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var updateDto = new UpdateMessageDTO
            {
                Id = EditingMessage.Id,
                Content = newContent
            };

            var result = await _apiClient.PutAsync<UpdateMessageDTO, MessageDTO>(
                ApiEndpoints.Message.ById(EditingMessage.Id), updateDto, ct);

            if (result.Success && result.Data != null)
            {
                EditingMessage.ApplyUpdate(result.Data);
                CancelEditMessage();
                await _notificationService.ShowSuccessAsync("Сообщение отредактировано");
            }
            else
            {
                await _notificationService.ShowErrorAsync($"Ошибка редактирования: {result.Error}");
            }
        });
    }

    [RelayCommand]
    private void CancelEditMessage()
    {
        EditingMessage = null;
        EditMessageContent = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteMessage(MessageViewModel? message)
    {
        if (message?.CanDelete != true) return;

        await SafeExecuteAsync(async ct =>
        {
            var result = await _apiClient.DeleteAsync(ApiEndpoints.Message.ById(message.Id), ct);

            if (result.Success)
            {
                message.MarkAsDeleted();
                await _notificationService.ShowSuccessAsync("Сообщение удалено");
            }
            else
            {
                await _notificationService.ShowErrorAsync($"Ошибка удаления: {result.Error}");
            }
        });
    }

    [RelayCommand]
    private async Task CopyMessageText(MessageViewModel? message)
    {
        if (message == null || string.IsNullOrEmpty(message.Content)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(message.Content);
                    await _notificationService.ShowInfoAsync("Текст скопирован");
                }
            }
        }
        catch (Exception ex)
        {
            await _notificationService.ShowErrorAsync($"Ошибка копирования: {ex.Message}");
        }
    }
}