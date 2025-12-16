using MessengerAPI.Hubs;
using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IPollService
    {
        Task<PollDTO?> GetPollAsync(int pollId, int userId);
        Task<MessageDTO> CreatePollAsync(PollDTO pollDto);
        Task<PollDTO> VoteAsync(PollVoteDTO voteDto);
    }

    public class PollService(MessengerDbContext context,IHubContext<ChatHub> hubContext,ILogger<PollService> logger) 
        : BaseService<PollService>(context, logger), IPollService
    {
        public async Task<PollDTO?> GetPollAsync(int pollId, int userId)
        {
            try
            {
                var poll = await _context.Polls.Include(p => p.PollOptions).Include(p => p.PollVotes).Include(p=> p.Message).
                    FirstOrDefaultAsync(p => p.Id == pollId);

                if (poll == null) return null;

                var voteCounts = await _context.PollVotes.Where(v => v.PollId == pollId).GroupBy(v => v.OptionId).
                    Select(g => new { OptionId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.OptionId, x => x.Count);

                var selectedOptionIds = await _context.PollVotes.Where(v => v.PollId == pollId && v.UserId == userId).
                    Select(v => v.OptionId).ToListAsync();

                var options = poll.PollOptions.OrderBy(o => o.Position).Select(o => new PollOptionDTO
                    {
                        Id = o.Id,
                        PollId = poll.Id,
                        Text = o.OptionText,
                        VotesCount = voteCounts.GetValueOrDefault(o.Id, 0),
                        Votes = !(poll.IsAnonymous ?? false)
                            ? [.. _context.PollVotes
                                .Where(v => v.OptionId == o.Id)
                                .Select(v => new PollVoteDTO
                                {
                                    PollId = v.PollId,
                                    UserId = v.UserId,
                                    OptionId = v.OptionId
                                })]
                            : []
                    })
                    .ToList();

                var message = await _context.Messages.FindAsync(poll.MessageId);
                var chatId = message?.ChatId ?? 0;

                var dto = new PollDTO
                {
                    Id = poll.Id,
                    MessageId = poll.MessageId,
                    ChatId = chatId,
                    Question = poll.Question,
                    IsAnonymous = poll.IsAnonymous ?? false,
                    AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
                    CreatedAt = poll.CreatedAt,
                    ClosesAt = poll.ClosesAt,
                    Options = options,
                    CanVote = !poll.PollVotes.Any(v => v.UserId == userId)
                };

                return dto;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting poll", pollId);
                throw;
            }
        }

        public async Task<MessageDTO> CreatePollAsync(PollDTO pollDto)
        {
            try
            {
                var message = new Message
                {
                    ChatId = pollDto.ChatId,
                    SenderId = pollDto.CreatedById,
                    Content = pollDto.Question,
                };

                _context.Messages.Add(message);
                await SaveChangesAsync();

                var poll = new Poll
                {
                    MessageId = message.Id,
                    Question = pollDto.Question,
                    IsAnonymous = pollDto.IsAnonymous,
                    AllowsMultipleAnswers = pollDto.AllowsMultipleAnswers,
                    ClosesAt = pollDto.ClosesAt
                };

                _context.Polls.Add(poll);
                await SaveChangesAsync();

                if (pollDto.Options != null && pollDto.Options.Count != 0)
                {
                    int position = 0;
                    foreach (var optionDto in pollDto.Options)
                    {
                        var option = new PollOption
                        {
                            PollId = poll.Id,
                            OptionText = optionDto.Text,
                            Position = position++
                        };
                        _context.PollOptions.Add(option);
                    }
                    await SaveChangesAsync();
                }

                var sender = await FindEntityAsync<User>(pollDto.CreatedById);

                var createdOptions = await _context.PollOptions
                    .Where(o => o.PollId == poll.Id)
                    .OrderBy(o => o.Position)
                    .ToListAsync();

                var messageDto = new MessageDTO
                {
                    Id = message.Id,
                    ChatId = message.ChatId,
                    SenderId = message.SenderId,
                    SenderName = sender?.DisplayName ?? sender?.Username,
                    Content = message.Content,
                    CreatedAt = message.CreatedAt,
                    Poll = new PollDTO
                    {
                        Id = poll.Id,
                        MessageId = message.Id,
                        ChatId = message.ChatId,
                        Question = poll.Question,
                        IsAnonymous = poll.IsAnonymous ?? false,
                        AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
                        CreatedAt = poll.CreatedAt,
                        ClosesAt = poll.ClosesAt,
                        Options = [.. createdOptions.Select(o => new PollOptionDTO
                        {
                            Id = o.Id,
                            PollId = poll.Id,
                            Text = o.OptionText,
                            VotesCount = 0,
                            Votes = []
                        })]
                    }
                };

                try
                {
                    await hubContext.Clients.Group(message.ChatId.ToString())
                        .SendAsync("ReceiveMessageDTO", messageDto);
                }
                catch (Exception hubEx)
                {
                    _logger.LogWarning(hubEx, "Failed to send poll via SignalR");
                }

                return messageDto;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "creating poll in chat", pollDto.ChatId);
                throw;
            }
        }

        public async Task<PollDTO> VoteAsync(PollVoteDTO voteDto)
        {
            try
            {
                var poll = await _context.Polls.Include(p => p.PollOptions).ThenInclude(o => o.PollVotes).FirstOrDefaultAsync(p => p.Id == voteDto.PollId);

                ValidateEntityExists(poll, "Poll", voteDto.PollId);

                var oldVotes = await _context.PollVotes.Where(v => v.PollId == voteDto.PollId && v.UserId == voteDto.UserId).ToListAsync();

                _context.PollVotes.RemoveRange(oldVotes);

                if (voteDto.OptionIds != null && voteDto.OptionIds.Count != 0)
                {
                    foreach (var optionId in voteDto.OptionIds)
                    {
                        var vote = new PollVote
                        {
                            PollId = voteDto.PollId,
                            OptionId = optionId,
                            UserId = voteDto.UserId,
                        };
                        _context.PollVotes.Add(vote);
                    }
                }
                else if (voteDto.OptionId.HasValue)
                {
                    var vote = new PollVote
                    {
                        PollId = voteDto.PollId,
                        OptionId = voteDto.OptionId.Value,
                        UserId = voteDto.UserId,
                    };
                    _context.PollVotes.Add(vote);
                }

                await SaveChangesAsync();

                var updatedPoll = await GetPollAsync(voteDto.PollId, voteDto.UserId) ?? throw new Exception("Failed to get updated poll");

                try
                {
                    var message = await _context.Messages.FindAsync(updatedPoll.MessageId);
                    if (message != null)
                        await hubContext.Clients.Group(message.ChatId.ToString()).SendAsync("ReceivePollUpdate", updatedPoll);
                }
                catch (Exception hubEx)
                {
                    _logger.LogWarning(hubEx, "Failed to send poll update via SignalR");
                }

                return updatedPoll;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "processing vote for poll", voteDto.PollId);
                throw;
            }
        }
    }
}