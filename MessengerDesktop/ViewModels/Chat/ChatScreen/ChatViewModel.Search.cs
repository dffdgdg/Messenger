using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    /// <summary>
    /// Скроллит к сообщению по ID с подсветкой.
    /// Если сообщение не загружено — подгружает окрестность через LoadMessagesAroundAsync.
    /// </summary>
    public async Task ScrollToMessageAsync(int messageId)
    {
        try
        {
            // Проверяем среди уже загруженных
            var existingMessage = Messages.FirstOrDefault(m => m.Id == messageId);

            if (existingMessage != null)
            {
                var index = Messages.IndexOf(existingMessage);
                ScrollToIndexRequested?.Invoke(index, false);
                HighlightMessage(existingMessage);
                return;
            }

            // Подгружаем окрестность
            var targetIndex = await _messageManager.LoadMessagesAroundAsync(messageId);

            if (targetIndex.HasValue)
            {
                await Task.Delay(100);
                ScrollToIndexRequested?.Invoke(targetIndex.Value, false);

                var targetMessage = Messages.FirstOrDefault(m => m.Id == messageId);
                if (targetMessage != null)
                    HighlightMessage(targetMessage);
            }
            else
            {
                Debug.WriteLine($"[ChatViewModel] Failed to load messages around {messageId}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] ScrollToMessage error: {ex.Message}");
        }
    }

    /// <summary>
    /// Подсвечивает сообщение и снимает подсветку через
    /// <see cref="AppConstants.HighlightDurationMs"/> мс.
    /// Сбрасывает подсветку у всех остальных сообщений.
    /// </summary>
    private void HighlightMessage(MessageViewModel message)
    {
        // Снимаем подсветку со всех
        foreach (var m in Messages)
            m.IsHighlighted = false;

        message.IsHighlighted = true;
        HighlightedMessageId = message.Id;

        // Автоматический сброс через заданный интервал
        _ = Task.Run(async () =>
        {
            await Task.Delay(AppConstants.HighlightDurationMs);
            Dispatcher.UIThread.Post(() =>
            {
                message.IsHighlighted = false;
                HighlightedMessageId = null;
            });
        });
    }

    /// <summary>Переход к результату поиска: выходит из режима поиска и скроллит.</summary>
    [RelayCommand]
    private async Task GoToSearchResult(MessageViewModel? searchResult)
    {
        if (searchResult == null) return;

        IsSearchMode = false;
        await ScrollToMessageAsync(searchResult.Id);
    }
}