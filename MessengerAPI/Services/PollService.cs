using MessengerAPI.Helpers;
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
        Task<MessageDTO> CreatePollAsync(PollDTO dto);
        Task<PollDTO> VoteAsync(PollVoteDTO voteDto);
    }

    public class PollService(MessengerDbContext context,IHubContext<ChatHub> hubContext,ILogger<PollService> logger)
        : BaseService<PollService>(context, logger), IPollService
    {
        public async Task<PollDTO?> GetPollAsync(int pollId, int userId)
        {
            var poll = await _context.Polls.Include(p => p.PollOptions).ThenInclude(o => o.PollVotes).Include(p => p.Message).AsNoTracking().FirstOrDefaultAsync(p => p.Id == pollId);

            return poll?.ToDto(userId);
        }

        public async Task<MessageDTO> CreatePollAsync(PollDTO dto)
        {
            // Создаём сообщение
            var message = new Message
            {
                ChatId = dto.ChatId,
                SenderId = dto.CreatedById,
                Content = dto.Question
            };

            _context.Messages.Add(message);
            await SaveChangesAsync();

            // Создаём опрос
            var poll = new Poll
            {
                MessageId = message.Id,
                Question = dto.Question,
                IsAnonymous = dto.IsAnonymous,
                AllowsMultipleAnswers = dto.AllowsMultipleAnswers,
                ClosesAt = dto.ClosesAt
            };

            _context.Polls.Add(poll);
            await SaveChangesAsync();

            // Добавляем варианты ответов
            if (dto.Options?.Count > 0)
            {
                var position = 0;
                foreach (var optionDto in dto.Options)
                {
                    _context.PollOptions.Add(new PollOption
                    {
                        PollId = poll.Id,
                        OptionText = optionDto.Text,
                        Position = position++
                    });
                }
                await SaveChangesAsync();
            }

            // Загружаем полное сообщение
            var createdMessage = await _context.Messages.Include(m => m.Sender).Include(m => m.Polls).ThenInclude(p => p.PollOptions).FirstOrDefaultAsync(m => m.Id == message.Id);

            EnsureNotNull(createdMessage, message.Id);

            var messageDto = createdMessage!.ToDto(dto.CreatedById);

            // Отправляем через SignalR
            await SendToGroupSafeAsync(dto.ChatId, "ReceiveMessageDTO", messageDto);

            _logger.LogInformation("Опрос создан в чате {ChatId}", dto.ChatId);

            return messageDto;
        }

        public async Task<PollDTO> VoteAsync(PollVoteDTO voteDto)
        {
            var poll = await _context.Polls.Include(p => p.PollOptions).ThenInclude(o => o.PollVotes).FirstOrDefaultAsync(p => p.Id == voteDto.PollId);

            EnsureNotNull(poll, voteDto.PollId);

            // Удаляем старые голоса пользователя
            var oldVotes = await _context.PollVotes.Where(v => v.PollId == voteDto.PollId && v.UserId == voteDto.UserId).ToListAsync();

            _context.PollVotes.RemoveRange(oldVotes);

            // Добавляем новые голоса
            var optionIds = voteDto.OptionIds?.Count > 0 ? voteDto.OptionIds : voteDto.OptionId.HasValue ? [voteDto.OptionId.Value] : [];

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

            // Получаем обновлённый опрос
            var updatedPoll = await GetPollAsync(voteDto.PollId, voteDto.UserId) ?? throw new InvalidOperationException("Не удалось получить обновлённый опрос");

            // Отправляем обновление через SignalR
            var message = await _context.Messages.FindAsync(updatedPoll.MessageId);
            if (message != null)
            {
                await SendToGroupSafeAsync(message.ChatId, "ReceivePollUpdate", updatedPoll);
            }

            _logger.LogInformation("Пользователь {UserId} проголосовал в опросе {PollId}",voteDto.UserId, voteDto.PollId);

            return updatedPoll;
        }

        private async Task SendToGroupSafeAsync<T>(int chatId, string method, T data)
        {
            try
            {
                await hubContext.Clients.Group(chatId.ToString()).SendAsync(method, data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить {Method} через SignalR", method);
            }
        }
    }
}