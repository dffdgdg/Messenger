using MessengerAPI.Helpers;
using MessengerAPI.Hubs;
using MessengerAPI.Model;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.Poll;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IPollService
    {
        Task<PollDTO?> GetPollAsync(int pollId, int userId);
        Task<MessageDTO> CreatePollAsync(CreatePollDTO dto, int createdByUserId);
        Task<PollDTO> VoteAsync(PollVoteDTO voteDto);
    }

    public class PollService(
        MessengerDbContext context,
        IHubContext<ChatHub> hubContext,
        ILogger<PollService> logger)
        : BaseService<PollService>(context, logger), IPollService
    {
        public async Task<PollDTO?> GetPollAsync(int pollId, int userId)
        {
            var poll = await _context.Polls
                .Include(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                .Include(p => p.Message)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == pollId);

            return poll?.ToDto(userId);
        }

        public async Task<MessageDTO> CreatePollAsync(CreatePollDTO dto, int createdByUserId)
        {
            // Вопрос опроса хранится ТОЛЬКО в Message.Content
            var message = new Message
            {
                ChatId = dto.ChatId,
                SenderId = createdByUserId,
                Content = dto.Question.Trim()
            };

            _context.Messages.Add(message);
            await SaveChangesAsync();

            // Poll без Question — вопрос уже в сообщении
            var poll = new Poll
            {
                MessageId = message.Id,
                IsAnonymous = dto.IsAnonymous,
                AllowsMultipleAnswers = dto.AllowsMultipleAnswers,
                ClosesAt = dto.ClosesAt
            };

            _context.Polls.Add(poll);
            await SaveChangesAsync();

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
                await SaveChangesAsync();
            }

            var createdMessage = await _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Polls).ThenInclude(p => p.PollOptions)
                .FirstOrDefaultAsync(m => m.Id == message.Id);

            EnsureNotNull(createdMessage, message.Id);

            var messageDto = createdMessage!.ToDto(createdByUserId);

            await SendToGroupSafeAsync(dto.ChatId, "ReceiveMessageDTO", messageDto);

            _logger.LogInformation("Опрос создан в чате {ChatId}", dto.ChatId);

            return messageDto;
        }

        public async Task<PollDTO> VoteAsync(PollVoteDTO voteDto)
        {
            var poll = await _context.Polls
                .Include(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                .Include(p => p.Message)
                .FirstOrDefaultAsync(p => p.Id == voteDto.PollId);

            EnsureNotNull(poll, voteDto.PollId);

            var oldVotes = await _context.PollVotes
                .Where(v => v.PollId == voteDto.PollId && v.UserId == voteDto.UserId)
                .ToListAsync();

            _context.PollVotes.RemoveRange(oldVotes);

            var optionIds = voteDto.OptionIds?.Count > 0
                ? voteDto.OptionIds
                : voteDto.OptionId.HasValue
                    ? [voteDto.OptionId.Value]
                    : [];

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

            var updatedPoll = await GetPollAsync(voteDto.PollId, voteDto.UserId)
                ?? throw new InvalidOperationException("Не удалось получить обновлённый опрос");

            var message = await _context.Messages.FindAsync(updatedPoll.MessageId);
            if (message != null)
            {
                await SendToGroupSafeAsync(message.ChatId, "ReceivePollUpdate", updatedPoll);
            }

            _logger.LogInformation("Пользователь {UserId} проголосовал в опросе {PollId}",
                voteDto.UserId, voteDto.PollId);

            return updatedPoll;
        }

        private async Task SendToGroupSafeAsync<T>(int chatId, string method, T data)
        {
            try
            {
                await hubContext.Clients.Group($"chat_{chatId}").SendAsync(method, data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить {Method} через SignalR", method);
            }
        }
    }
}