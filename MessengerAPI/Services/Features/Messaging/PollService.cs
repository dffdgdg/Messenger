using MessengerAPI.Services.Base;
using MessengerAPI.Services.Infrastructure.Security;

namespace MessengerAPI.Services.Messaging;

public partial class PollService(MessengerDbContext context, IAccessControlService accessControl, IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder, ILogger<PollService> logger) : BaseService<PollService>(context, logger), IPollService
{
    public async Task<Result<PollDto>> GetPollAsync(int pollId, int userId)
    {
        var poll = await _context.Polls.Include(p => p.PollOptions).ThenInclude(o => o.PollVotes)
            .Include(p => p.Message).AsNoTracking().FirstOrDefaultAsync(p => p.Id == pollId);

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

        var access = await accessControl.EnsureMemberOfAsync(voteDto.UserId, poll.Message!.ChatId);
        if (access.IsFailure) return access.As<PollDto>();

        List<int> optionIds;
        if (voteDto.OptionIds?.Count > 0)
            optionIds = voteDto.OptionIds;
        else if (voteDto.OptionId.HasValue)
            optionIds = [voteDto.OptionId.Value];
        else
            optionIds = [];

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

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<PollDto>();

        var updatedPollResult = await GetPollAsync(voteDto.PollId, voteDto.UserId);
        if (updatedPollResult.IsFailure) return updatedPollResult;

        if (poll.Message != null)
        {
            var affectedChatIds = await _context.Messages.Where(m => m.Id == poll.MessageId || m.ForwardedFromMessageId == poll.MessageId)
                .Select(m => m.ChatId).Distinct().ToListAsync();

            foreach (var chatId in affectedChatIds)
            {
                await hubNotifier.SendToChatAsync(chatId, "ReceivePollUpdate", updatedPollResult.Value!);
            }
        }

        LogUserVoted(voteDto.UserId, voteDto.PollId);

        return updatedPollResult;
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "Опрос создан в чате {ChatId}")]
    private partial void LogPollCreated(int chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} проголосовал в опросе {PollId}")]
    private partial void LogUserVoted(int userId, int pollId);

    #endregion
}