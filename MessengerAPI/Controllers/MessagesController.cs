using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MessengerAPI.Hubs;

namespace MessengerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagesController(MessengerDbContext context, IHubContext<ChatHub> hubContext) : ControllerBase
    {
        private readonly MessengerDbContext _context = context;
        private readonly IHubContext<ChatHub> _hubContext = hubContext;

        [HttpPost]
        public async Task<IActionResult> CreateMessage(MessageDTO messageDto)
        {
            try
            {
                var message = new Message
                {
                    ChatId = messageDto.ChatId,
                    SenderId = messageDto.SenderId,
                    Content = messageDto.Content
                };

                if (messageDto.Poll != null)
                {
                    var poll = new Poll
                    {
                        Question = messageDto.Poll.Question,
                        IsAnonymous = messageDto.Poll.IsAnonymous,
                        AllowsMultipleAnswers = messageDto.Poll.AllowsMultipleAnswers,
                        CreatedById = messageDto.SenderId,
                        PollOptions = [.. messageDto.Poll.Options
                            .Select(o => new PollOption
                            {
                                OptionText = o.Text,
                                Position = o.Position
                            })]
                    };

                    message.Polls.Add(poll);
                }

                _context.Messages.Add(message);
                await _context.SaveChangesAsync();

                // Загружаем созданное сообщение со всеми связанными данными
                var createdMessage = await _context.Messages
                    .Include(m => m.Sender)
                    .ThenInclude(s => s.DepartmentNavigation)
                    .Include(m => m.Polls)
                    .ThenInclude(p => p.PollOptions)
                    .FirstOrDefaultAsync(m => m.Id == message.Id);

                if (createdMessage != null)
                {
                    var fullMessageDto = new MessageDTO
                    {
                        Id = createdMessage.Id,
                        ChatId = createdMessage.ChatId,
                        SenderId = createdMessage.SenderId,
                        Content = createdMessage.Content,
                        CreatedAt = createdMessage.CreatedAt,
                        SenderName = createdMessage.Sender?.DisplayName ?? createdMessage.Sender?.Username,
                        SenderAvatarUrl = !string.IsNullOrEmpty(createdMessage.Sender?.Avatar)
                            ? $"{Request.Scheme}://{Request.Host}{createdMessage.Sender.Avatar}"
                            : null,
                        IsOwn = true // Для отправителя сообщение всегда своё
                    };

                    if (createdMessage.Polls.Count != 0)
                    {
                        var createdPoll = createdMessage.Polls.First();
                        fullMessageDto.Poll = new PollDTO
                        {
                            Id = createdPoll.Id,
                            MessageId = createdPoll.MessageId,
                            CreatedById = createdPoll.CreatedById,
                            ChatId = createdMessage.ChatId,
                            Question = createdPoll.Question,
                            IsAnonymous = createdPoll.IsAnonymous ?? false,
                            AllowsMultipleAnswers = createdPoll.AllowsMultipleAnswers ?? false,
                            CreatedAt = createdPoll.CreatedAt,
                            ClosesAt = createdPoll.ClosesAt,
                            Options = [.. createdPoll.PollOptions.Select(o => new PollOptionDTO
                            {
                                Id = o.Id,
                                PollId = o.PollId,
                                Text = o.OptionText,
                                Position = o.Position,
                                VotesCount = 0
                            })],
                            CanVote = true
                        };
                    }

                    // Отправляем уведомление через SignalR
                    await _hubContext.Clients.Group(createdMessage.ChatId.ToString())
                        .SendAsync("ReceiveMessageDTO", fullMessageDto);

                    return Ok(fullMessageDto);
                }

                return BadRequest("Failed to create message");
            }
            catch (Exception ex)
            {
                return BadRequest($"Ошибка при отправке сообщения: {ex.Message}");
            }
        }

        [HttpGet("chat/{chatId}")]
        public async Task<ActionResult<PagedMessagesDTO>> GetChatMessages(
            int chatId,
            [FromQuery] int? userId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                var query = _context.Messages
                    .Include(m => m.Sender)
                    .Include(m => m.Polls)
                    .ThenInclude(p => p.PollOptions)
                    .ThenInclude(o => o.PollVotes)
                    .Where(m => m.ChatId == chatId)
                    .OrderByDescending(m => m.CreatedAt);

                var totalCount = await query.CountAsync();
                var skipCount = (page - 1) * pageSize;
                var hasMoreMessages = totalCount > skipCount + pageSize;

                var messages = await query
                    .Skip(skipCount)
                    .Take(pageSize)
                    .ToListAsync();

                var messageDtos = messages.Select(m =>
                {
                    var dto = new MessageDTO
                    {
                        Id = m.Id,
                        ChatId = m.ChatId,
                        SenderId = m.SenderId,
                        Content = m.Content,
                        CreatedAt = m.CreatedAt,
                        SenderName = m.Sender?.DisplayName ?? m.Sender?.Username,
                        SenderAvatarUrl = !string.IsNullOrEmpty(m.Sender?.Avatar)
                            ? $"{Request.Scheme}://{Request.Host}{m.Sender.Avatar}"
                            : null,
                        IsOwn = userId.HasValue && m.SenderId == userId
                    };

                    var poll = m.Polls.FirstOrDefault();
                    if (poll != null)
                    {
                        dto.Poll = new PollDTO
                        {
                            Id = poll.Id,
                            MessageId = poll.MessageId,
                            CreatedById = poll.CreatedById,
                            ChatId = m.ChatId,
                            Question = poll.Question,
                            IsAnonymous = poll.IsAnonymous ?? false,
                            AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
                            CreatedAt = poll.CreatedAt,
                            ClosesAt = poll.ClosesAt,
                            Options = [.. poll.PollOptions.Select(o => new PollOptionDTO
                            {
                                Id = o.Id,
                                PollId = o.PollId,
                                Text = o.OptionText,
                                Position = o.Position,
                                VotesCount = o.PollVotes.Count
                            })]
                        };

                        if (userId.HasValue)
                        {
                            dto.Poll.SelectedOptionIds = [.. poll.PollOptions
                                .SelectMany(o => o.PollVotes)
                                .Where(v => v.UserId == userId)
                                .Select(v => v.OptionId)];

                            dto.Poll.CanVote = dto.Poll.SelectedOptionIds.Count == 0;
                        }
                    }

                    return dto;
                }).ToList();

                messageDtos.Reverse();

                return Ok(new PagedMessagesDTO
                {
                    Messages = messageDtos,
                    CurrentPage = page,
                    HasMoreMessages = hasMoreMessages
                });
            }
            catch (Exception ex)
            {
                return BadRequest($"Ошибка при получении сообщений: {ex.Message}");
            }
        }
    }
}
