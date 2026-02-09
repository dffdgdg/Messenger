using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Infrastructure.Configuration;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    public async Task ScrollToMessageAsync(int messageId)
    {
        try
        {
            var existingMessage = Messages.FirstOrDefault(m => m.Id == messageId);

            if (existingMessage != null)
            {
                var index = Messages.IndexOf(existingMessage);
                ScrollToIndexRequested?.Invoke(index, false);
                HighlightMessage(existingMessage);
                return;
            }

            var targetIndex = await _messageManager.LoadMessagesAroundAsync(messageId);

            if (targetIndex.HasValue)
            {
                await Task.Delay(100);
                ScrollToIndexRequested?.Invoke(targetIndex.Value, false);

                var targetMessage = Messages.FirstOrDefault(m => m.Id == messageId);
                if (targetMessage != null)
                {
                    HighlightMessage(targetMessage);
                }
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

    private void HighlightMessage(MessageViewModel message)
    {
        foreach (var m in Messages)
        {
            m.IsHighlighted = false;
        }

        message.IsHighlighted = true;
        HighlightedMessageId = message.Id;

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

    [RelayCommand]
    private async Task GoToSearchResult(MessageViewModel? searchResult)
    {
        if (searchResult == null) return;

        IsSearchMode = false;
        await ScrollToMessageAsync(searchResult.Id);
    }
}