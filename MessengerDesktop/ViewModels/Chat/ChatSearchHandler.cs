using MessengerDesktop.ViewModels.Chat.Managers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat
{
    public sealed partial class ChatSearchHandler(ChatContext context, ChatMessageManager messageManager) : ChatFeatureHandler(context)
    {
        [ObservableProperty] private bool _isSearchMode;
        [ObservableProperty] private int? _highlightedMessageId;

        public async Task ScrollToMessageAsync(int messageId)
        {
            try
            {
                var existing = messageManager.Messages.FirstOrDefault(
                    m => m.Id == messageId);

                if (existing != null)
                {
                    var index = messageManager.Messages.IndexOf(existing);
                    Ctx.RequestScrollToIndex(index);
                    Highlight(existing);
                    return;
                }

                var targetIndex = await messageManager
                    .LoadMessagesAroundAsync(messageId);

                if (targetIndex.HasValue)
                {
                    await Task.Delay(100);
                    Ctx.RequestScrollToIndex(targetIndex.Value);

                    var target = messageManager.Messages
                        .FirstOrDefault(m => m.Id == messageId);
                    if (target != null) Highlight(target);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Search] ScrollToMessage error: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task GoToSearchResult(MessageViewModel? result)
        {
            if (result == null) return;
            IsSearchMode = false;
            await ScrollToMessageAsync(result.Id);
        }

        private void Highlight(MessageViewModel message)
        {
            foreach (var m in messageManager.Messages)
                m.IsHighlighted = false;

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
    }
}
