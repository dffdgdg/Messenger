using API.Repositories.Abstarctions;
using API.Services.Base;
using API.Services.Infrastructure.Security;

namespace API.Services.Messaging;

public partial class PollService(MessengerDbContext context, IPollRepository pollRepository, IMessageRepository messageRepository,
    IAccessControlService accessControl, IHubNotifier hubNotifier, IUrlBuilder urlBuilder, ILogger<PollService> logger)
    : BaseService<PollService>(context, logger), IPollService
{
    public async Task<Result<PollDto>> GetPollAsync(int pollId, int userId)
    {
        var poll = await pollRepository.FindByIdWithDetailsAsync(pollId);

        if (poll is null)
            return Result<PollDto>.NotFound($"Опрос с ID {pollId} не найден");

        var access = await accessControl.EnsureMemberOfAsync(userId, poll.Message!.ChatId);
        if (access.IsFailure) return access.As<PollDto>();

        return Result<PollDto>.Success(poll.ToDto(userId));
    }

    public async Task<Result<MessageDto>> CreatePollAsync(CreatePollDto dto, int createdByUserId)
    {
        var access = await accessControl.EnsureMemberOfAsync(createdByUserId, dto.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (string.IsNullOrWhiteSpace(dto.Question))
            return Result<MessageDto>.Failure("Вопрос опроса обязателен");

        if (dto.Options.Count < 2)
            return Result<MessageDto>.Failure("Опрос должен содержать минимум 2 варианта");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var message = new UserMessage
        {
            ChatId = dto.ChatId,
            SenderId = createdByUserId,
            Content = dto.Question.Trim()
        };

        messageRepository.Add(message);
        await _context.SaveChangesAsync();

        var poll = new Poll
        {
            MessageId = message.Id,
            IsAnonymous = dto.IsAnonymous,
            AllowsMultipleAnswers = dto.AllowsMultipleAnswers,
            ClosesAt = dto.ClosesAt
        };

        pollRepository.Add(poll);
        await _context.SaveChangesAsync();

        for (var i = 0; i < dto.Options.Count; i++)
        {
            var opt = dto.Options[i];
            pollRepository.AddOption(new PollOption
            {
                PollId = poll.Id,
                OptionText = opt.Text.Trim(),
                Position = opt.Position > 0 ? opt.Position : i
            });
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var createdMessage = await messageRepository.FindUserMessageWithIncludesAsync(message.Id);

        if (createdMessage is null)
            return Result<MessageDto>.Internal("Не удалось загрузить созданное сообщение");

        var messageDto = createdMessage.ToDto(createdByUserId, urlBuilder);

        await hubNotifier.SendToChatAsync(dto.ChatId, "ReceiveMessageDto", messageDto);

        LogPollCreated(dto.ChatId);

        return Result<MessageDto>.Success(messageDto);
    }

    public async Task<Result<PollDto>> VoteAsync(PollVoteDto voteDto)
    {
        var poll = await pollRepository.FindByIdWithDetailsAsync(voteDto.PollId);

        if (poll is null)
            return Result<PollDto>.NotFound($"Опрос {voteDto.PollId} не найден");

        var access = await accessControl.EnsureMemberOfAsync(voteDto.UserId, poll.Message!.ChatId);
        if (access.IsFailure) return access.As<PollDto>();

        if (poll.ClosesAt < DateTime.UtcNow)
            return Result<PollDto>.Failure("Опрос уже закрыт.");

        var optionIds = ResolveOptionIds(voteDto);

        var validOptionIds = poll.PollOptions.Select(o => o.Id).ToHashSet();
        var invalidIds = optionIds.Where(id => !validOptionIds.Contains(id)).ToList();
        if (invalidIds.Count > 0)
            return Result<PollDto>.Failure($"Невалидные варианты: {string.Join(", ", invalidIds)}");

        var oldVotes = await pollRepository.GetUserVotesAsync(voteDto.PollId, voteDto.UserId);
        pollRepository.RemoveVotes(oldVotes);

        foreach (var optionId in optionIds)
        {
            pollRepository.AddVote(new PollVote
            {
                PollId = voteDto.PollId,
                OptionId = optionId,
                UserId = voteDto.UserId
            });
        }

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<PollDto>();

        var updatedPollResult = await GetPollAsync(voteDto.PollId, voteDto.UserId);
        if (updatedPollResult.IsFailure) return updatedPollResult;

        await BroadcastPollUpdateAsync(poll.MessageId, updatedPollResult.Value!);

        LogUserVoted(voteDto.UserId, voteDto.PollId);

        return updatedPollResult;
    }

    public async Task<Result<PollDto>> ClosePollAsync(int pollId, int userId)
    {
        var poll = await pollRepository.FindByIdWithDetailsAsync(pollId);

        if (poll is null)
            return Result<PollDto>.NotFound($"Опрос {pollId} не найден");

        if (poll.ClosesAt < DateTime.UtcNow)
            return Result<PollDto>.Failure("Опрос уже закрыт");

        var message = poll.Message;
        if (message is null)
            return Result<PollDto>.Failure("Связанное сообщение не найдено");

        var isAuthor = message.SenderId == userId;
        var isAdminOrOwner = await accessControl.IsAdminAsync(userId, message.ChatId)
                          || await accessControl.IsOwnerAsync(userId, message.ChatId);

        if (!isAuthor && !isAdminOrOwner)
            return Result<PollDto>.Failure("Недостаточно прав для закрытия опроса");

        var closedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await pollRepository.CloseAsync(pollId, closedAt);

        var updatedPollResult = await GetPollAsync(pollId, userId);
        if (updatedPollResult.IsFailure) return updatedPollResult;

        await BroadcastPollUpdateAsync(poll.MessageId, updatedPollResult.Value!);

        LogPollClosed(pollId, userId);

        return updatedPollResult;
    }

    #region Private

    private static List<int> ResolveOptionIds(PollVoteDto voteDto)
    {
        if (voteDto.OptionIds?.Count > 0)
            return voteDto.OptionIds;

        if (voteDto.OptionId.HasValue)
            return [voteDto.OptionId.Value];

        return [];
    }

    private async Task BroadcastPollUpdateAsync(int messageId, PollDto updatedPoll)
    {
        var affectedChatIds = await messageRepository.GetForwardedToChatIdsAsync(messageId);

        foreach (var chatId in affectedChatIds)
            await hubNotifier.SendToChatAsync(chatId, "ReceivePollUpdate", updatedPoll);
    }

    #endregion

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "Опрос {PollId} досрочно закрыт пользователем {UserId}")]
    private partial void LogPollClosed(int pollId, int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Опрос создан в чате {ChatId}")]
    private partial void LogPollCreated(int chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} проголосовал в опросе {PollId}")]
    private partial void LogUserVoted(int userId, int pollId);

    #endregion
}