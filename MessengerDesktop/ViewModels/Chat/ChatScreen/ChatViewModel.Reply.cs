using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    /// <summary>Сообщение, на которое пользователь отвечает (null — режим выключен).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplyMode))]
    private MessageViewModel? _replyingToMessage;

    /// <summary>Активен ли режим ответа.</summary>
    public bool IsReplyMode => ReplyingToMessage != null;

    /// <summary>
    /// Начать ответ на сообщение.
    /// Автоматически выходит из режима редактирования.
    /// </summary>
    [RelayCommand]
    private void StartReply(MessageViewModel? message)
    {
        if (message == null || message.IsDeleted) return;

        CancelEditMessage();
        ReplyingToMessage = message;
    }

    /// <summary>Отменить ответ.</summary>
    [RelayCommand]
    private void CancelReply() => ReplyingToMessage = null;

    /// <summary>
    /// Перейти к оригинальному сообщению, на которое был дан ответ.
    /// Если сообщение не загружено — подгружает окрестность и скроллит.
    /// </summary>
    [RelayCommand]
    private async Task ScrollToReplyOriginal(MessageViewModel? message)
    {
        if (message?.ReplyToMessageId == null) return;

        var targetId = message.ReplyToMessageId.Value;

        // Ищем среди уже загруженных сообщений
        var existing = Messages.FirstOrDefault(m => m.Id == targetId);

        if (existing != null)
        {
            HighlightAndScrollToMessage(existing);
        }
        else
        {
            // Подгружаем окрестность целевого сообщения
            var ct = _loadingCts?.Token ?? System.Threading.CancellationToken.None;
            var targetIndex = await _messageManager.LoadMessagesAroundAsync(targetId, ct);

            if (targetIndex.HasValue && targetIndex.Value < Messages.Count)
            {
                HighlightAndScrollToIndex(targetIndex.Value);
            }
        }
    }

    /// <summary>Подсветить сообщение и скроллить к нему. Подсветка снимается через 2 секунды.</summary>
    private void HighlightAndScrollToMessage(MessageViewModel target)
    {
        target.IsHighlighted = true;
        ScrollToMessageRequested?.Invoke(target, true);
        ScheduleHighlightReset(target);
    }

    /// <summary>Подсветить сообщение по индексу и скроллить. Подсветка снимается через 2 секунды.</summary>
    private void HighlightAndScrollToIndex(int index)
    {
        var target = Messages[index];
        target.IsHighlighted = true;
        ScrollToIndexRequested?.Invoke(index, true);
        ScheduleHighlightReset(target);
    }

    /// <summary>Снимает подсветку через 2 секунды.</summary>
    private static void ScheduleHighlightReset(MessageViewModel target)
    {
        _ = Task.Delay(2000).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => target.IsHighlighted = false));
    }
}