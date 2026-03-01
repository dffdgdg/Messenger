using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    /// <summary>Сообщение, находящееся в режиме редактирования (null — режим выключен).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    private MessageViewModel? _editingMessage;

    /// <summary>Текст редактируемого сообщения (привязан к TextBox).</summary>
    [ObservableProperty]
    private string _editMessageContent = string.Empty;

    /// <summary>Активен ли режим редактирования.</summary>
    public bool IsEditMode => EditingMessage != null;

    /// <summary>
    /// Войти в режим редактирования сообщения.
    /// Отменяет текущий ответ и предыдущее редактирование.
    /// </summary>
    [RelayCommand]
    private void StartEditMessage(MessageViewModel? message)
    {
        if (message?.CanEdit != true) return;

        CancelReply();
        CancelEditMessage();

        EditingMessage = message;
        EditMessageContent = message.Content ?? string.Empty;
    }

    /// <summary>
    /// Сохранить отредактированное сообщение.
    /// Если текст не изменился — просто выходит из режима.
    /// Если текст пустой — удаляет сообщение.
    /// </summary>
    [RelayCommand]
    private async Task SaveEditMessage()
    {
        if (EditingMessage == null) return;

        var newContent = EditMessageContent?.Trim();

        // Текст не изменился — ничего не делаем
        if (newContent == EditingMessage.Content)
        {
            CancelEditMessage();
            return;
        }

        // Пустой текст = удаление
        if (string.IsNullOrWhiteSpace(newContent))
        {
            await DeleteMessage(EditingMessage);
            CancelEditMessage();
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var updateDto = new UpdateMessageDto
            {
                Id = EditingMessage.Id,
                Content = newContent
            };

            var result = await _apiClient.PutAsync<UpdateMessageDto, MessageDto>(
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

    /// <summary>Выйти из режима редактирования без сохранения.</summary>
    [RelayCommand]
    private void CancelEditMessage()
    {
        EditingMessage = null;
        EditMessageContent = string.Empty;
    }

    /// <summary>Удалить сообщение (soft-delete на сервере).</summary>
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

    /// <summary>Скопировать текст сообщения в буфер обмена.</summary>
    [RelayCommand]
    private async Task CopyMessageText(MessageViewModel? message)
    {
        if (message == null || string.IsNullOrEmpty(message.Content)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
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