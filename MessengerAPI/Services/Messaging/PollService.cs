using MessengerAPI.Services.Base;

namespace MessengerAPI.Services.Messaging;

public interface IPollService
{
    Task<Result<PollDto>> GetPollAsync(int pollId, int userId);
    Task<Result<MessageDto>> CreatePollAsync(CreatePollDto dto, int createdByUserId);
    Task<Result<PollDto>> VoteAsync(PollVoteDto voteDto);
}

public partial class PollService(MessengerDbContext context,IAccessControlService accessControl,IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder,ILogger<PollService> logger) : BaseService<PollService>(context, logger), IPollService
{
    public async Task<Result<PollDto>> GetPollAsync(int pollId, int userId)
    {
        var poll = await _context.Polls.Include(p => p.PollOptions).ThenInclude(o => o.PollVotes).Include(p => p.Message).AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pollId);

        if (poll is null)
            return Result<PollDto>.NotFound($"Опрос с ID {pollId} не найден");

        var accessResult = await accessControl.CheckIsMemberAsync(userId, poll.Message!.ChatId);
        if (accessResult.IsFailure)
            return Result<PollDto>.FromFailure(accessResult);

        return Result<PollDto>.Success(poll.ToDto(userId));
    }

    public async Task<Result<MessageDto>> CreatePollAsync(CreatePollDto dto, int createdByUserId)
    {
        var accessResult = await accessControl.CheckIsMemberAsync(createdByUserId, dto.ChatId);
        if (accessResult.IsFailure)
            return Result<MessageDto>.FromFailure(accessResult);

        if (string.IsNullOrWhiteSpace(dto.Question))
            return Result<MessageDto>.Failure("Вопрос опроса обязателен");

        if (dto.Options.Count < 2)
            return Result<MessageDto>.Failure("Опрос должен содержать минимум 2 варианта");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var message = new Message
        {
            ChatId = dto.ChatId,
            SenderId = createdByUserId,
            Content = dto.Question.Trim()
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        var poll = new Poll
        {
            MessageId = message.Id,
            IsAnonymous = dto.IsAnonymous,
            AllowsMultipleAnswers = dto.AllowsMultipleAnswers,
            ClosesAt = dto.ClosesAt
        };

        _context.Polls.Add(poll);
        await _context.SaveChangesAsync();

        for (int i = 0; i < dto.Options.Count; i++)
        {
            var opt = dto.Options[i];
            _context.PollOptions.Add(new PollOption
            {
                PollId = poll.Id,
                OptionText = opt.Text.Trim(),
                Position = opt.Position > 0 ? opt.Position : i
            });
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var createdMessage = await _context.Messages.Include(m => m.Sender).Include(m => m.Polls).ThenInclude(p => p.PollOptions).FirstOrDefaultAsync(m => m.Id == message.Id);

        if (createdMessage is null)
            return Result<MessageDto>.Internal("Не удалось загрузить созданное сообщение");

        var messageDto = createdMessage.ToDto(createdByUserId, urlBuilder);

        await hubNotifier.SendToChatAsync(dto.ChatId, "ReceiveMessageDto", messageDto);

        LogPollCreated(dto.ChatId);

        return Result<MessageDto>.Success(messageDto);
    }

    public async Task<Result<PollDto>> VoteAsync(PollVoteDto voteDto)
    {
        var poll = await _context.Polls.Include(p => p.PollOptions).ThenInclude(o => o.PollVotes).Include(p => p.Message).FirstOrDefaultAsync(p => p.Id == voteDto.PollId);

        if (poll is null)
            return Result<PollDto>.NotFound($"Опрос {voteDto.PollId} не найден");

        var accessResult = await accessControl.CheckIsMemberAsync(voteDto.UserId, poll.Message!.ChatId);
        if (accessResult.IsFailure)
            return Result<PollDto>.FromFailure(accessResult);

        var optionIds = voteDto.OptionIds?.Count > 0
            ? voteDto.OptionIds : voteDto.OptionId.HasValue
            ? [voteDto.OptionId.Value] : [];

        var validOptionIds = poll.PollOptions.Select(o => o.Id).ToHashSet();
        var invalidIds = optionIds.Where(id => !validOptionIds.Contains(id)).ToList();
        if (invalidIds.Count > 0)
            return Result<PollDto>.Failure($"Невалидные варианты: {string.Join(", ", invalidIds)}");

        var oldVotes = await _context.PollVotes.Where(v => v.PollId == voteDto.PollId && v.UserId == voteDto.UserId).ToListAsync();

        _context.PollVotes.RemoveRange(oldVotes);

        foreach (var optionId in optionIds)
        {
            _context.PollVotes.Add(new PollVote
            {
                PollId = voteDto.PollId,
                OptionId = optionId,
                UserId = voteDto.UserId
            });
        }

        var saveResult = await SaveChangesAsync();
        if (saveResult.IsFailure)
            return Result<PollDto>.FromFailure(saveResult);

        var updatedPollResult = await GetPollAsync(voteDto.PollId, voteDto.UserId);
        if (updatedPollResult.IsFailure)
            return updatedPollResult;

        if (poll.Message != null)
        {
            await hubNotifier.SendToChatAsync(poll.Message.ChatId, "ReceivePollUpdate", updatedPollResult.Value!);
        }

        LogUserVoted(voteDto.UserId, voteDto.PollId);

        return updatedPollResult;
    }

    #region Log messages

    [LoggerMessage(Level = LogLevel.Information, Message = "Опрос создан в чате {ChatId}")]
    private partial void LogPollCreated(int chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} проголосовал в опросе {PollId}")]
    private partial void LogUserVoted(int userId, int pollId);

    #endregion
}