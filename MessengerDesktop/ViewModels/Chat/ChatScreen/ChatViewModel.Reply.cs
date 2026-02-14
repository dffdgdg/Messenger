using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplyMode))]
    private MessageViewModel? _replyingToMessage;

    /// <summary>
    /// Активен ли режим ответа
    /// </summary>
    public bool IsReplyMode => ReplyingToMessage != null;

    /// <summary>
    /// Начать ответ на сообщение
    /// </summary>
    [RelayCommand]
    private void StartReply(MessageViewModel? message)
    {
        if (message == null || message.IsDeleted) return;

        // Если в режиме редактирования — выходим из него
        CancelEditMessage();

        ReplyingToMessage = message;
    }

    /// <summary>
    /// Отменить ответ
    /// </summary>
    [RelayCommand]
    private void CancelReply() => ReplyingToMessage = null;

    /// <summary>
    /// Скролл к оригинальному сообщению, на которое дан ответ
    /// </summary>
    [RelayCommand]
    private async Task ScrollToReplyOriginal(MessageViewModel? message)
    {
        if (message?.ReplyToMessageId == null) return;

        var targetId = message.ReplyToMessageId.Value;

        // Ищем среди загруженных сообщений
        var existing = Messages.FirstOrDefault(m => m.Id == targetId);

        if (existing != null)
        {
            // Сообщение уже загружено — скроллим и подсвечиваем
            existing.IsHighlighted = true;
            ScrollToMessageRequested?.Invoke(existing, true);

            // Снять подсветку через 2 секунды
            _ = Task.Delay(2000).ContinueWith(_ =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => existing.IsHighlighted = false));
        }
        else
        {
            // Сообщение не загружено — загружаем around и скроллим
            var ct = _loadingCts?.Token ?? System.Threading.CancellationToken.None;
            var targetIndex = await _messageManager.LoadMessagesAroundAsync(targetId, ct);

            if (targetIndex < Messages.Count)
            {
                var target = Messages[targetIndex.Value];
                target.IsHighlighted = true;
                ScrollToIndexRequested?.Invoke(targetIndex.Value, true);

                _ = Task.Delay(2000).ContinueWith(_ =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => target.IsHighlighted = false));
            }
        }
    }
}