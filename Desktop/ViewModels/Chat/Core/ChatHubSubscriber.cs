using Desktop.ViewModels.Chat.Context;
using Desktop.ViewModels.Chat.Managers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Desktop.ViewModels.Chat.Core;

public sealed class ChatHubSubscriber(ChatContext ctx, ChatMessageManager messageManager, Action<int> onUnreadCountChanged, Func<Task> onReconnected) : IDisposable
{
    private bool _subscribed;

    public void Subscribe()
    {
        if (_subscribed) return;

        ctx.Hub.MessageReceivedGlobally += OnMessageReceived;
        ctx.Hub.MessageUpdatedGlobally += OnMessageUpdated;
        ctx.Hub.PollUpdatedGlobally += OnPollUpdated;
        ctx.Hub.MessageDeletedGlobally += OnMessageDeleted;
        ctx.Hub.MessageRead += OnMessageRead;
        ctx.Hub.UnreadCountChanged += OnUnreadCountChanged;
        ctx.Hub.Reconnected += OnReconnected;

        // Typing подписывается внутри ChatTypingHandler
        // InfoPanel подписывается внутри ChatInfoPanelHandler

        _subscribed = true;
    }

    private void OnMessageReceived(MessageDto msg)
    {
        if (ctx.IsDisposed || msg.ChatId != ctx.ChatId) return;

        Dispatcher.UIThread.Post(() => messageManager.AddReceivedMessage(msg));
    }

    private void OnMessageUpdated(MessageDto msg)
    {
        if (ctx.IsDisposed || msg.ChatId != ctx.ChatId) return;
        Dispatcher.UIThread.Post(() => messageManager.HandleMessageUpdated(msg));
    }

    private void OnMessageDeleted(int messageId, int chatId)
    {
        if (ctx.IsDisposed || chatId != ctx.ChatId) return;
        Dispatcher.UIThread.Post(() => messageManager.HandleMessageDeleted(messageId));
    }
    private void OnPollUpdated(PollDto poll)
    {
        if (ctx.IsDisposed) return;
        Dispatcher.UIThread.Post(() => messageManager.HandlePollUpdated(poll));
    }

    private void OnMessageRead(int chatId, int userId, int? lastReadId, DateTime? readAt)
    {
        if (ctx.IsDisposed || chatId != ctx.ChatId) return;
        if (!lastReadId.HasValue) return;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var msg in messageManager.Messages.Where(m => m.Id <= lastReadId.Value
                    && m.SenderId == ctx.CurrentUserId))
            {
                msg.IsRead = true;
            }
        });
    }

    private void OnUnreadCountChanged(int chatId, int count)
    {
        if (ctx.IsDisposed || chatId != ctx.ChatId) return;
        onUnreadCountChanged(count);
    }

    private void OnReconnected()
    {
        if (ctx.IsDisposed) return;
        _ = onReconnected();
    }

    public void Dispose()
    {
        Debug.WriteLine($"[HubSub] Disposed chat={ctx.ChatId}");
        if (!_subscribed) return;

        ctx.Hub.MessageReceivedGlobally -= OnMessageReceived;
        ctx.Hub.MessageUpdatedGlobally -= OnMessageUpdated;
        ctx.Hub.PollUpdatedGlobally -= OnPollUpdated;
        ctx.Hub.MessageDeletedGlobally -= OnMessageDeleted;
        ctx.Hub.MessageRead -= OnMessageRead;
        ctx.Hub.UnreadCountChanged -= OnUnreadCountChanged;
        ctx.Hub.Reconnected -= OnReconnected;

        _subscribed = false;
    }
}