using MessengerDesktop.ViewModels.Chat.Managers;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Единая точка подписки/отписки на все hub-события для чата.
/// Маршрутизирует входящие события к handlers.
/// </summary>
public sealed class ChatHubSubscriber(ChatContext ctx, ChatMessageManager messageManager,
    ChatVoiceHandler voice, Action<int> onUnreadCountChanged, Func<Task> onReconnected) : IDisposable
{
    private bool _subscribed;

    public void Subscribe()
    {
        if (_subscribed) return;

        ctx.Hub.MessageReceivedGlobally += OnMessageReceived;
        ctx.Hub.MessageUpdatedGlobally += OnMessageUpdated;
        ctx.Hub.MessageDeletedGlobally += OnMessageDeleted;
        ctx.Hub.MessageRead += OnMessageRead;
        ctx.Hub.UnreadCountChanged += OnUnreadCountChanged;
        ctx.Hub.TranscriptionStatusChanged += OnTranscriptionStatusChanged;
        ctx.Hub.TranscriptionCompleted += OnTranscriptionCompleted;
        ctx.Hub.Reconnected += OnReconnected;

        // Typing подписывается внутри ChatTypingHandler
        // InfoPanel подписывается внутри ChatInfoPanelHandler

        _subscribed = true;
    }

    private void OnMessageReceived(MessageDto msg)
    {
        if (ctx.IsDisposed || msg.ChatId != ctx.ChatId) return;

        Dispatcher.UIThread.Post(() =>
        {
            messageManager.AddReceivedMessage(msg);

            var added = messageManager.Messages.LastOrDefault(
                m => m.Id == msg.Id);
            if (added != null)
                voice.StartTranscriptionPollingIfNeeded(added);
        });
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

    private void OnTranscriptionStatusChanged(VoiceTranscriptionDto transcription)
    {
        if (ctx.IsDisposed || transcription.ChatId != ctx.ChatId) return;

        Dispatcher.UIThread.Post(() =>
        {
            var message = messageManager.Messages.FirstOrDefault(m => m.Id == transcription.MessageId);
            if (message == null) return;

            message.UpdateTranscription(transcription.Status, transcription.Transcription);
            voice.StartTranscriptionPollingIfNeeded(message);
        });
    }

    private void OnTranscriptionCompleted(VoiceTranscriptionDto transcription)
    {
        if (ctx.IsDisposed || transcription.ChatId != ctx.ChatId) return;

        Dispatcher.UIThread.Post(() =>
        {
            var message = messageManager.Messages.FirstOrDefault(m => m.Id == transcription.MessageId);
            message?.UpdateTranscription(transcription.Status, transcription.Transcription);
        });
    }

    private void OnReconnected()
    {
        if (ctx.IsDisposed) return;
        _ = onReconnected();
    }

    public void Dispose()
    {
        if (!_subscribed) return;

        ctx.Hub.MessageReceivedGlobally -= OnMessageReceived;
        ctx.Hub.MessageUpdatedGlobally -= OnMessageUpdated;
        ctx.Hub.MessageDeletedGlobally -= OnMessageDeleted;
        ctx.Hub.MessageRead -= OnMessageRead;
        ctx.Hub.UnreadCountChanged -= OnUnreadCountChanged;
        ctx.Hub.TranscriptionStatusChanged -= OnTranscriptionStatusChanged;
        ctx.Hub.TranscriptionCompleted -= OnTranscriptionCompleted;
        ctx.Hub.Reconnected -= OnReconnected;

        _subscribed = false;
    }
}
