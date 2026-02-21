using MessengerAPI.Common;
using MessengerAPI.Model;
using MessengerShared.DTO.ReadReceipt;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services;

public interface IReadReceiptService
{
    Task<ReadReceiptResponseDTO> MarkAsReadAsync(int userId, MarkAsReadDTO request);
    Task<ReadReceiptResponseDTO> MarkMessageAsReadAsync(int userId, int chatId, int messageId);
    Task<int> GetUnreadCountAsync(int userId, int chatId);
    Task<AllUnreadCountsDTO> GetAllUnreadCountsAsync(int userId);
    Task<Dictionary<int, int>> GetUnreadCountsForChatsAsync(int userId, IEnumerable<int> chatIds);
    Task MarkAllAsReadAsync(int userId, int chatId);
    Task<ChatReadInfoDTO?> GetChatReadInfoAsync(int userId, int chatId);
}

public class ReadReceiptService(
    MessengerDbContext context,
    ILogger<ReadReceiptService> logger) : IReadReceiptService
{
    public async Task<ReadReceiptResponseDTO> MarkAsReadAsync(
        int userId, MarkAsReadDTO request)
    {
        var member = await context.ChatMembers
            .FirstOrDefaultAsync(cm =>
                cm.ChatId == request.ChatId && cm.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Пользователь {userId} не является участником чата {request.ChatId}");

        var targetMessageId = await DetermineTargetMessageIdAsync(request);

        if (targetMessageId > 0
            && (!member.LastReadMessageId.HasValue
                || targetMessageId > member.LastReadMessageId.Value))
        {
            member.LastReadMessageId = targetMessageId;
            member.LastReadAt = AppDateTime.UtcNow;
            await context.SaveChangesAsync();

            logger.LogDebug(
                "Пользователь {UserId} прочитал до {MessageId} в чате {ChatId}",
                userId, targetMessageId, request.ChatId);
        }

        var lastReadId = member.LastReadMessageId ?? 0;
        var unreadCount = await CountUnreadAsync(userId, request.ChatId, lastReadId);

        return CreateResponse(member, unreadCount);
    }

    public async Task<ReadReceiptResponseDTO> MarkMessageAsReadAsync(
        int userId, int chatId, int messageId)
    {
        var member = await context.ChatMembers
            .FirstOrDefaultAsync(cm =>
                cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return new ReadReceiptResponseDTO { ChatId = chatId, UnreadCount = 0 };

        if (!member.LastReadMessageId.HasValue
            || messageId > member.LastReadMessageId.Value)
        {
            var messageExists = await context.Messages.AnyAsync(m =>
                m.Id == messageId
                && m.ChatId == chatId
                && m.IsDeleted != true);

            if (messageExists)
            {
                member.LastReadMessageId = messageId;
                member.LastReadAt = AppDateTime.UtcNow;
                await context.SaveChangesAsync();

                logger.LogDebug(
                    "Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}",
                    userId, messageId, chatId);
            }
        }

        var lastReadId = member.LastReadMessageId ?? 0;
        var unreadCount = await CountUnreadAsync(userId, chatId, lastReadId);

        return CreateResponse(member, unreadCount);
    }

    public async Task<ChatReadInfoDTO?> GetChatReadInfoAsync(int userId, int chatId)
    {
        var member = await context.ChatMembers.AsNoTracking()
            .FirstOrDefaultAsync(cm =>
                cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return null;

        var lastReadId = member.LastReadMessageId ?? 0;

        var unreadInfo = await context.Messages
            .Where(m => m.ChatId == chatId
                && m.Id > lastReadId
                && m.IsDeleted != true
                && m.SenderId != userId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                FirstId = g.Min(m => m.Id)
            })
            .FirstOrDefaultAsync();

        return new ChatReadInfoDTO
        {
            ChatId = chatId,
            LastReadMessageId = member.LastReadMessageId,
            LastReadAt = member.LastReadAt,
            UnreadCount = unreadInfo?.Count ?? 0,
            FirstUnreadMessageId = unreadInfo?.Count > 0 ? unreadInfo.FirstId : null
        };
    }

    public async Task<int> GetUnreadCountAsync(int userId, int chatId)
    {
        var member = await context.ChatMembers.AsNoTracking()
            .FirstOrDefaultAsync(cm =>
                cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return 0;

        return await CountUnreadAsync(userId, chatId, member.LastReadMessageId ?? 0);
    }

    public async Task<AllUnreadCountsDTO> GetAllUnreadCountsAsync(int userId)
    {
        var counts = await context.ChatMembers
            .Where(cm => cm.UserId == userId)
            .Select(cm => new
            {
                cm.ChatId,
                UnreadCount = context.Messages.Count(m =>
                    m.ChatId == cm.ChatId
                    && m.Id > (cm.LastReadMessageId ?? 0)
                    && m.IsDeleted != true
                    && m.SenderId != userId)
            })
            .Where(x => x.UnreadCount > 0)
            .AsNoTracking()
            .ToListAsync();

        var result = counts.ConvertAll(x => new UnreadCountDTO(x.ChatId, x.UnreadCount));

        return new AllUnreadCountsDTO
        {
            Chats = result,
            TotalUnread = result.Sum(x => x.UnreadCount)
        };
    }

    public async Task<Dictionary<int, int>> GetUnreadCountsForChatsAsync(
        int userId, IEnumerable<int> chatIds)
    {
        var chatIdList = chatIds.ToList();

        var counts = await context.ChatMembers
            .Where(cm => cm.UserId == userId && chatIdList.Contains(cm.ChatId))
            .Select(cm => new
            {
                cm.ChatId,
                UnreadCount = context.Messages.Count(m =>
                    m.ChatId == cm.ChatId
                    && m.Id > (cm.LastReadMessageId ?? 0)
                    && m.IsDeleted != true
                    && m.SenderId != userId)
            })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.ChatId, x => x.UnreadCount);

        foreach (var chatId in chatIdList)
            counts.TryAdd(chatId, 0);

        return counts;
    }

    public async Task MarkAllAsReadAsync(int userId, int chatId)
        => await MarkAsReadAsync(userId, new MarkAsReadDTO { ChatId = chatId });

    #region Private

    private async Task<int> CountUnreadAsync(int userId, int chatId, int lastReadId)
        => await context.Messages.CountAsync(m =>
            m.ChatId == chatId
            && m.Id > lastReadId
            && m.IsDeleted != true
            && m.SenderId != userId);

    private async Task<int> DetermineTargetMessageIdAsync(MarkAsReadDTO request)
    {
        if (request.MessageId.HasValue)
        {
            var exists = await context.Messages.AnyAsync(m =>
                m.Id == request.MessageId.Value
                && m.ChatId == request.ChatId
                && m.IsDeleted != true);

            if (!exists)
                throw new KeyNotFoundException($"Сообщение {request.MessageId} не найдено в чате {request.ChatId}");

            return request.MessageId.Value;
        }

        return await context.Messages
            .Where(m => m.ChatId == request.ChatId && m.IsDeleted != true)
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
    }

    private static ReadReceiptResponseDTO CreateResponse(
        ChatMember member, int unreadCount) => new()
        {
            ChatId = member.ChatId,
            LastReadMessageId = member.LastReadMessageId,
            LastReadAt = member.LastReadAt,
            UnreadCount = unreadCount
        };

    #endregion
}