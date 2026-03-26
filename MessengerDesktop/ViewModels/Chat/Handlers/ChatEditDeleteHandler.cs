using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatEditDeleteHandler : ChatFeatureHandler
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    public partial MessageViewModel? EditingMessage { get; set; }

    [ObservableProperty]
    public partial string EditMessageContent { get; set; } = string.Empty;

    public bool IsEditMode => EditingMessage != null;

    public ChatEditDeleteHandler(ChatContext context) : base(context)
        => Ctx.CompositionModeReset += OnCompositionReset;

    [RelayCommand]
    private void StartEdit(MessageViewModel? message)
    {
        if (message?.CanEdit != true) return;

        Ctx.ResetCompositionModes();

        EditingMessage = message;
        EditMessageContent = message.Content ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveEdit()
    {
        if (EditingMessage == null) return;

        var newContent = EditMessageContent?.Trim();

        if (newContent == EditingMessage.Content)
        {
            CancelEdit();
            return;
        }

        if (string.IsNullOrWhiteSpace(newContent))
        {
            await DeleteMessage(EditingMessage);
            CancelEdit();
            return;
        }

        var updateDto = new UpdateMessageDto
        {
            Id = EditingMessage.Id,
            Content = newContent
        };

        var result = await Ctx.Api.PutAsync<UpdateMessageDto, MessageDto>(ApiEndpoints.Messages.ById(EditingMessage.Id), updateDto);

        if (result.Success && result.Data != null)
        {
            EditingMessage.ApplyUpdate(result.Data);
            CancelEdit();
            await Ctx.Notifications.ShowSuccessAsync("Сообщение отредактировано");
        }
        else
        {
            await Ctx.Notifications.ShowErrorAsync($"Ошибка редактирования: {result.Error}");
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingMessage = null;
        EditMessageContent = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteMessage(MessageViewModel? message)
    {
        if (message?.CanDelete != true) return;

        var result = await Ctx.Api.DeleteAsync(
            ApiEndpoints.Messages.ById(message.Id));

        if (result.Success)
        {
            message.MarkAsDeleted();
            await Ctx.Notifications.ShowSuccessAsync("Сообщение удалено");
        }
        else
        {
            await Ctx.Notifications.ShowErrorAsync($"Ошибка удаления: {result.Error}");
        }
    }

    [RelayCommand]
    private async Task CopyMessageText(MessageViewModel? message)
    {
        if (message == null || string.IsNullOrEmpty(message.Content))
            return;

        try
        {
            if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(message.Content);
                    await Ctx.Notifications.ShowInfoAsync("Текст скопирован");
                }
            }
        }
        catch (Exception ex)
        {
            await Ctx.Notifications.ShowErrorAsync($"Ошибка копирования: {ex.Message}");
        }
    }

    private void OnCompositionReset()
    {
        // Вызывается при StartReply/StartForward
        // Тихо отменяем, без рекурсивного ResetCompositionModes
        if (IsEditMode) CancelEdit();
    }

    public override void Dispose()
    {
        Ctx.CompositionModeReset -= OnCompositionReset;
        base.Dispose();
    }
}
