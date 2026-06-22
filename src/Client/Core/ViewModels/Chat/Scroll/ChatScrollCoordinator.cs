using Core.ViewModels.Chat.Context;
using Core.ViewModels.Chat.Managers;

namespace Core.ViewModels.Chat.Scroll;

/// <summary>
/// Координирует скролл и отметку сообщений прочитанными.
/// </summary>
public sealed class ChatScrollCoordinator(ChatContext ctx, ChatMessageManager messageManager)
{
    private DateTime _lastMarkAsReadTime = DateTime.MinValue;

    public void ScrollToBottom() => ctx.RequestScrollToBottom();
    public void ScrollToMessage(MessageViewModel msg, bool highlight)
        => ctx.RequestScrollToMessage(msg, highlight);
    public void ScrollToIndex(int index, bool highlight)
        => ctx.RequestScrollToIndex(index, highlight);

    public async Task OnMessageVisibleAsync(MessageViewModel message)
    {
        if (ctx.IsDisposed) return;
        if (!message.IsUnread || message.SenderId == ctx.CurrentUserId) return;

        message.IsUnread = false;
        messageManager.MarkAsReadLocally(message.Id);
        await ctx.Hub.MarkMessageAsReadAsync(ctx.ChatId, message.Id);
    }

    public async Task MarkMessagesAsReadAsync()
    {
        if (ctx.IsDisposed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastMarkAsReadTime).TotalSeconds < AppConstants.MarkAsReadCooldownSeconds)
            return;

        _lastMarkAsReadTime = now;
        await ctx.Hub.MarkChatAsReadAsync(ctx.ChatId);
    }
}