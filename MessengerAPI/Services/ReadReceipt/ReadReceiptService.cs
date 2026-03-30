namespace MessengerAPI.Services.ReadReceipt;

public interface IReadReceiptService
{
    Task<Result<ReadReceiptResponseDto>> MarkAsReadAsync(int userId, MarkAsReadDto request);
    Task<Result<ReadReceiptResponseDto>> MarkMessageAsReadAsync(int userId, int chatId, int messageId);
    Task<Result<int>> GetUnreadCountAsync(int userId, int chatId);
    Task<Result<AllUnreadCountsDto>> GetAllUnreadCountsAsync(int userId);
    Task<Dictionary<int, int>> GetUnreadCountsForChatsAsync(int userId, IEnumerable<int> chatIds);
    Task<Result> MarkAllAsReadAsync(int userId, int chatId);
    Task<Result<ChatReadInfoDto>> GetChatReadInfoAsync(int userId, int chatId);
}

public partial class ReadReceiptService(MessengerDbContext context, AppDateTime appDateTime,
    ILogger<ReadReceiptService> logger) : IReadReceiptService
{
    public async Task<Result<ReadReceiptResponseDto>> MarkAsReadAsync(int userId, MarkAsReadDto request)
    {
        var member = await context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == request.ChatId && cm.UserId == userId);

        if (member is null)
            return Result<ReadReceiptResponseDto>.Failure($"Пользователь {userId} не является участником чата {request.ChatId}");

        var targetResult = await DetermineTargetMessageIdAsync(request);
        if (targetResult.IsFailure)
            return Result<ReadReceiptResponseDto>.FromFailure(targetResult);

        var targetMessageId = targetResult.Value;

        if (targetMessageId > 0
            && (!member.LastReadMessageId.HasValue || targetMessageId > member.LastReadMessageId.Value))
        {
            member.LastReadMessageId = targetMessageId;
            member.LastReadAt = appDateTime.UtcNow;
            await context.SaveChangesAsync();

            LogReadReceipt(userId, targetMessageId, request.ChatId);
        }

        var lastReadId = member.LastReadMessageId ?? 0;
        var unreadCount = await CountUnreadAsync(userId, request.ChatId, lastReadId);

        return Result<ReadReceiptResponseDto>.Success(CreateResponse(member, unreadCount));
    }

    public async Task<Result<ReadReceiptResponseDto>> MarkMessageAsReadAsync(int userId, int chatId, int messageId)
    {
        var member = await context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return Result<ReadReceiptResponseDto>.Success(new ReadReceiptResponseDto { ChatId = chatId, UnreadCount = 0 });

        if (!member.LastReadMessageId.HasValue || messageId > member.LastReadMessageId.Value)
        {
            var messageExists = await context.Messages.AnyAsync(m =>
                m.Id == messageId && m.ChatId == chatId && m.IsDeleted != true);

            if (messageExists)
            {
                member.LastReadMessageId = messageId;
                member.LastReadAt = appDateTime.UtcNow;
                await context.SaveChangesAsync();

                LogReadReceipt(userId, messageId, chatId);
            }
        }

        var lastReadId = member.LastReadMessageId ?? 0;
        var unreadCount = await CountUnreadAsync(userId, chatId, lastReadId);

        return Result<ReadReceiptResponseDto>.Success(CreateResponse(member, unreadCount));
    }

    public async Task<Result<ChatReadInfoDto>> GetChatReadInfoAsync(int userId, int chatId)
    {
        var member = await context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return Result<ChatReadInfoDto>.Failure($"Пользователь {userId} не является участником чата {chatId}");

        var lastReadId = member.LastReadMessageId ?? 0;

        var unreadInfo = await context.Messages.Where(m => m.ChatId == chatId && m.Id > lastReadId && m.IsDeleted != true && m.SenderId != userId).GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(),
            FirstId = g.Min(m => m.Id)
        })
        .FirstOrDefaultAsync();

        return Result<ChatReadInfoDto>.Success(new ChatReadInfoDto
        {
            ChatId = chatId,
            LastReadMessageId = member.LastReadMessageId,
            LastReadAt = member.LastReadAt,
            UnreadCount = unreadInfo?.Count ?? 0,
            FirstUnreadMessageId = unreadInfo?.Count > 0 ? unreadInfo.FirstId : null
        });
    }

    public async Task<Result<int>> GetUnreadCountAsync(int userId, int chatId)
    {
        var member = await context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return Result<int>.Success(0);

        var count = await CountUnreadAsync(userId, chatId, member.LastReadMessageId ?? 0);
        return Result<int>.Success(count);
    }

    public async Task<Result<AllUnreadCountsDto>> GetAllUnreadCountsAsync(int userId)
    {
        var counts = await context.ChatMembers.Where(cm => cm.UserId == userId).Select(cm => new
        {
            cm.ChatId,
            UnreadCount = context.Messages.Count(m =>
            m.ChatId == cm.ChatId
            && m.Id > (cm.LastReadMessageId ?? 0)
            && m.IsDeleted != true
            && m.SenderId != userId)
        }).Where(x => x.UnreadCount > 0).AsNoTracking().ToListAsync();

        var result = counts.ConvertAll(x => new UnreadCountDto(x.ChatId, x.UnreadCount));

        return Result<AllUnreadCountsDto>.Success(new AllUnreadCountsDto
        {
            Chats = result,
            TotalUnread = result.Sum(x => x.UnreadCount)
        });
    }

    public async Task<Dictionary<int, int>> GetUnreadCountsForChatsAsync(int userId, IEnumerable<int> chatIds)
    {
        var chatIdList = chatIds.ToList();

        var counts = await context.ChatMembers.Where(cm => cm.UserId == userId && chatIdList.Contains(cm.ChatId)).Select(cm => new
        {
            cm.ChatId,
            UnreadCount = context.Messages.Count(m => m.ChatId == cm.ChatId && m.Id > (cm.LastReadMessageId ?? 0) && m.IsDeleted != true && m.SenderId != userId)})
            .AsNoTracking().ToDictionaryAsync(x => x.ChatId, x => x.UnreadCount);

        foreach (var chatId in chatIdList)
            counts.TryAdd(chatId, 0);

        return counts;
    }

    public async Task<Result> MarkAllAsReadAsync(int userId, int chatId)
    {
        var result = await MarkAsReadAsync(userId, new MarkAsReadDto { ChatId = chatId });
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    #region Private

    private async Task<int> CountUnreadAsync(int userId, int chatId, int lastReadId)
        => await context.Messages.CountAsync(m =>m.ChatId == chatId && m.Id > lastReadId && m.IsDeleted != true && m.SenderId != userId);

    private async Task<Result<int>> DetermineTargetMessageIdAsync(MarkAsReadDto request)
    {
        if (request.MessageId.HasValue)
        {
            var exists = await context.Messages.AnyAsync(m => m.Id == request.MessageId.Value && m.ChatId == request.ChatId && m.IsDeleted != true);

            if (!exists)
                return Result<int>.Failure($"Сообщение {request.MessageId} не найдено в чате {request.ChatId}");

            return Result<int>.Success(request.MessageId.Value);
        }

        var lastId = await context.Messages.Where(m => m.ChatId == request.ChatId && m.IsDeleted != true).OrderByDescending(m => m.Id).Select(m => m.Id).FirstOrDefaultAsync();

        return Result<int>.Success(lastId);
    }

    private static ReadReceiptResponseDto CreateResponse(ChatMember member, int unreadCount) => new()
    {
        ChatId = member.ChatId,
        LastReadMessageId = member.LastReadMessageId,
        LastReadAt = member.LastReadAt,
        UnreadCount = unreadCount
    };
    #endregion

    #region Log messages

    [LoggerMessage(Level = LogLevel.Debug, Message = "Пользователь {UserId} прочитал до {MessageId} в чате {ChatId}")]
    private partial void LogReadReceipt(int userId, int messageId, int chatId);

    #endregion
}