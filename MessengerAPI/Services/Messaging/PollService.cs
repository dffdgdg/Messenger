using MessengerAPI.Common;
using MessengerAPI.Mapping;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.Dto.Message;
using MessengerShared.Dto.Poll;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services.Messaging;

public interface IPollService
{
    Task<Result<PollDto>> GetPollAsync(int pollId, int userId);
    Task<Result<MessageDto>> CreatePollAsync(CreatePollDto dto, int createdByUserId);
    Task<Result<PollDto>> VoteAsync(PollVoteDto voteDto);
}

public class PollService(
    MessengerDbContext context,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder,
    ILogger<PollService> logger)
    : BaseService<PollService>(context, logger), IPollService
{
    public async Task<Result<PollDto>> GetPollAsync(int pollId, int userId)
    {
        var poll = await _context.Polls
            .Include(p => p.PollOptions).ThenInclude(o => o.PollVotes)
            .Include(p => p.Message)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pollId);

        if (poll is null)
            return Result<PollDto>.Failure($"Опрос с ID {pollId} не найден");

        return Result<PollDto>.Success(poll.ToDto(userId));
    }

    public async Task<Result<MessageDto>> CreatePollAsync(CreatePollDto dto, int createdByUserId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
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

            if (dto.Options.Count > 0)
            {
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
            }

            await transaction.CommitAsync();

            var createdMessage = await _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Polls).ThenInclude(p => p.PollOptions)
                .FirstOrDefaultAsync(m => m.Id == message.Id);

            if (createdMessage is null)
                return Result<MessageDto>.Failure("Не удалось загрузить созданное сообщение");

            var messageDto = createdMessage.ToDto(createdByUserId, urlBuilder);

            await hubNotifier.SendToChatAsync(
                dto.ChatId, "ReceiveMessageDto", messageDto);

            _logger.LogInformation("Опрос создан в чате {ChatId}", dto.ChatId);

            return Result<MessageDto>.Success(messageDto);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<Result<PollDto>> VoteAsync(PollVoteDto voteDto)
    {
        var poll = await _context.Polls
            .Include(p => p.PollOptions).ThenInclude(o => o.PollVotes)
            .Include(p => p.Message)
            .FirstOrDefaultAsync(p => p.Id == voteDto.PollId);

        if (poll is null)
            return Result<PollDto>.Failure($"Опрос {voteDto.PollId} не найден");

        var optionIds = voteDto.OptionIds?.Count > 0
            ? voteDto.OptionIds
            : voteDto.OptionId.HasValue
                ? [voteDto.OptionId.Value]
                : [];

        var validOptionIds = poll.PollOptions.Select(o => o.Id).ToHashSet();
        var invalidIds = optionIds.Where(id => !validOptionIds.Contains(id)).ToList();
        if (invalidIds.Count > 0)
            return Result<PollDto>.Failure($"Невалидные варианты: {string.Join(", ", invalidIds)}");

        var oldVotes = await _context.PollVotes
            .Where(v => v.PollId == voteDto.PollId && v.UserId == voteDto.UserId).ToListAsync();

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

        await SaveChangesAsync();

        var updatedPollResult = await GetPollAsync(voteDto.PollId, voteDto.UserId);
        if (updatedPollResult.IsFailure)
            return updatedPollResult;

        if (poll.Message != null)
        {
            await hubNotifier.SendToChatAsync(poll.Message.ChatId, "ReceivePollUpdate", updatedPollResult.Value!);
        }

        _logger.LogInformation("Пользователь {UserId} проголосовал в опросе {PollId}", voteDto.UserId, voteDto.PollId);

        return updatedPollResult;
    }
}