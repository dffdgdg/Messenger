using Shared.Contracts.ReadReceipt;
using Shared.HubProtocol;
using Microsoft.AspNetCore.SignalR;

namespace API.Web.Hubs;

public sealed partial class MessengerHub
{
    public async Task<ChatReadInfoDto?> GetReadInfo(int chatId)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return null;

        var result = await readReceiptService.GetChatReadInfoAsync(userId.Value, chatId);
        return result.UnwrapOrDefault(logger);
    }

    public async Task MarkAsRead(int chatId, int? messageId = null)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return;

        var result = await readReceiptService.MarkAsReadAsync(userId.Value, new MarkAsReadDto { ChatId = chatId, MessageId = messageId });

        if (!result.TryUnwrap(out var receipt, logger)) return;

        await Clients.Caller.SendAsync(HubMethods.Chat.UnreadCountUpdated, chatId, receipt.UnreadCount);

        await Clients.OthersInGroup(ChatGroup(chatId)).SendAsync(HubMethods.Chat.MessageRead, chatId, userId.Value, receipt.LastReadMessageId, receipt.LastReadAt);

        logger.LogDebug("Пользователь {UserId} прочитал чат {ChatId}, unread={UnreadCount}", userId.Value, chatId, receipt.UnreadCount);
    }

    public async Task MarkMessageAsRead(int chatId, int messageId)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return;

        var result = await readReceiptService.MarkMessageAsReadAsync(
            userId.Value, chatId, messageId);

        if (!result.TryUnwrap(out var receipt, logger)) return;

        await Clients.Caller.SendAsync(HubMethods.Chat.UnreadCountUpdated, chatId, receipt.UnreadCount);

        await Clients.OthersInGroup(ChatGroup(chatId)).SendAsync(HubMethods.Chat.MessageRead, chatId, userId.Value, receipt.LastReadMessageId, receipt.LastReadAt);

        logger.LogDebug("Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}", userId.Value, messageId, chatId);
    }

    public async Task<AllUnreadCountsDto> GetUnreadCounts()
    {
        var userId = GetCurrentUserId();
        var fallback = new AllUnreadCountsDto { Chats = [], TotalUnread = 0 };
        if (!userId.HasValue) return fallback;

        var result = await readReceiptService.GetAllUnreadCountsAsync(userId.Value);
        return result.UnwrapOrFallback(fallback, logger);
    }
}