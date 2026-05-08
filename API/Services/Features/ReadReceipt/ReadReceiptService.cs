using API.Repositories.Abstarctions;
using API.Services.Infrastructure.Bundles;

namespace API.Services.ReadReceipt;

public partial class ReadReceiptService(IReadReceiptRepository readReceiptRepository, TimeBundle time, ILogger<ReadReceiptService> logger) : IReadReceiptService
{
    private readonly AppDateTime _appDateTime = time.AppDateTime;

    public async Task<Result<ReadReceiptResponseDto>> MarkAsReadAsync(int userId, MarkAsReadDto request)
    {
        var member = await readReceiptRepository.FindMemberAsync(request.ChatId, userId);

        if (member is null)
            return Result<ReadReceiptResponseDto>.Failure($"Пользователь {userId} не является участником чата {request.ChatId}");

        var targetResult = await DetermineTargetMessageIdAsync(request);
        if (targetResult.IsFailure) return targetResult.As<ReadReceiptResponseDto>();

        var targetMessageId = targetResult.Value;

        if (targetMessageId > 0 &&
            (!member.LastReadMessageId.HasValue || targetMessageId > member.LastReadMessageId.Value))
        {
            await readReceiptRepository.UpdateReadPointerAsync(member, targetMessageId, _appDateTime.UtcNow);
            LogReadReceipt(userId, targetMessageId, request.ChatId);
        }

        var lastReadId = member.LastReadMessageId ?? 0;
        var unreadCount = await readReceiptRepository.CountUnreadAsync(
            request.ChatId, userId, lastReadId);

        return Result<ReadReceiptResponseDto>.Success(CreateResponse(member, unreadCount));
    }

    public async Task<Result<ReadReceiptResponseDto>> MarkMessageAsReadAsync(int userId, int chatId, int messageId)
    {
        var member = await readReceiptRepository.FindMemberAsync(chatId, userId);

        if (member is null)
            return Result<ReadReceiptResponseDto>.Success(new ReadReceiptResponseDto { ChatId = chatId, UnreadCount = 0 });

        if (!member.LastReadMessageId.HasValue || messageId > member.LastReadMessageId.Value)
        {
            var messageExists = await readReceiptRepository.MessageExistsAsync(messageId, chatId);

            if (messageExists)
            {
                await readReceiptRepository.UpdateReadPointerAsync(member, messageId, _appDateTime.UtcNow);
                LogReadReceipt(userId, messageId, chatId);
            }
        }

        var lastReadId = member.LastReadMessageId ?? 0;
        var unreadCount = await readReceiptRepository.CountUnreadAsync(chatId, userId, lastReadId);

        return Result<ReadReceiptResponseDto>.Success(CreateResponse(member, unreadCount));
    }

    public async Task<Result<ChatReadInfoDto>> GetChatReadInfoAsync(int userId, int chatId)
    {
        var member = await readReceiptRepository.FindMemberReadonlyAsync(chatId, userId);

        if (member is null)
            return Result<ChatReadInfoDto>.Failure($"Пользователь {userId} не является участником чата {chatId}");

        var lastReadId = member.LastReadMessageId ?? 0;
        var unreadInfo = await readReceiptRepository.GetUnreadInfoAsync(chatId, userId, lastReadId);

        return Result<ChatReadInfoDto>.Success(new ChatReadInfoDto
        {
            ChatId = chatId,
            LastReadMessageId = member.LastReadMessageId,
            LastReadAt = member.LastReadAt,
            UnreadCount = unreadInfo?.Count ?? 0,
            FirstUnreadMessageId = unreadInfo?.Count > 0 ? unreadInfo.FirstUnreadId : null
        });
    }

    public async Task<Result<int>> GetUnreadCountAsync(int userId, int chatId)
    {
        var member = await readReceiptRepository.FindMemberReadonlyAsync(chatId, userId);

        if (member is null)
            return Result<int>.Success(0);

        var count = await readReceiptRepository.CountUnreadAsync(
            chatId, userId, member.LastReadMessageId ?? 0);

        return Result<int>.Success(count);
    }

    public async Task<Result<AllUnreadCountsDto>> GetAllUnreadCountsAsync(int userId)
    {
        var projections = await readReceiptRepository.GetAllUnreadCountsAsync(userId);

        var result = projections.ConvertAll(x => new UnreadCountDto(x.ChatId, x.UnreadCount));

        return Result<AllUnreadCountsDto>.Success(new AllUnreadCountsDto
        {
            Chats = result,
            TotalUnread = result.Sum(x => x.UnreadCount)
        });
    }

    public async Task<Dictionary<int, int>> GetUnreadCountsForChatsAsync(int userId, IEnumerable<int> chatIds)
        => await readReceiptRepository.GetUnreadCountsAsync(userId, chatIds);

    public async Task<Result> MarkAllAsReadAsync(int userId, int chatId)
    {
        var result = await MarkAsReadAsync(userId, new MarkAsReadDto { ChatId = chatId });
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    #region Private

    private async Task<Result<int>> DetermineTargetMessageIdAsync(MarkAsReadDto request)
    {
        if (request.MessageId.HasValue)
        {
            var exists = await readReceiptRepository.MessageExistsAsync(request.MessageId.Value, request.ChatId);

            if (!exists)
                return Result<int>.Failure($"Сообщение {request.MessageId} не найдено в чате {request.ChatId}");

            return Result<int>.Success(request.MessageId.Value);
        }

        var lastId = await readReceiptRepository.GetLastMessageIdAsync(request.ChatId);
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

    #region Log

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Пользователь {UserId} прочитал до {MessageId} в чате {ChatId}")]
    private partial void LogReadReceipt(int userId, int messageId, int chatId);

    #endregion
}