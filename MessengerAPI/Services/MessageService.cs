using MessengerAPI.Hubs;
using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IMessageService
    {
        Task<MessageDTO> CreateMessageAsync(MessageDTO messageDto, HttpRequest request);
        Task<PagedMessagesDTO> GetChatMessagesAsync(int chatId, int? userId, int page, int pageSize, HttpRequest request);
    }

    public class MessageService(MessengerDbContext context,IHubContext<ChatHub> hubContext, ILogger<MessageService> logger) 
        : BaseService<MessageService>(context, logger), IMessageService
    {
        public async Task<MessageDTO> CreateMessageAsync(MessageDTO messageDto, HttpRequest request)
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
                        PollOptions = messageDto.Poll.Options?.Select(o => new PollOption { OptionText = o.Text, Position = o.Position}).ToList() ?? []
                    };

                    message.Polls.Add(poll);
                }

                _context.Messages.Add(message);
                await SaveChangesAsync();

                if (messageDto.Files != null && messageDto.Files.Count > 0)
                {
                    var uploadedIds = messageDto.Files.Where(f => f.Id > 0).Select(f => f.Id).ToList();

                    if (uploadedIds.Count > 0)
                    {
                        var uploadedFiles = await _context.MessageFiles.Where(mf => uploadedIds.Contains(mf.Id)).ToListAsync();
                        foreach (var uf in uploadedFiles)
                        {
                            uf.MessageId = message.Id;
                        }
                    }

                    var toCreate = messageDto.Files.Where(f => f.Id <= 0).ToList();
                    foreach (var f in toCreate)
                    {
                        var mf = new MessageFile
                        {
                            FileName = f.FileName,
                            ContentType = f.ContentType,
                            Path = f.Url,
                            MessageId = message.Id
                        };
                        _context.MessageFiles.Add(mf);
                    }

                    if (uploadedIds.Count > 0 || toCreate.Count > 0)
                        await SaveChangesAsync();
                }

                var createdMessage = await _context.Messages.Include(m => m.Sender).Include(m => m.Polls)
                    .ThenInclude(p => p.PollOptions).Include(m => m.MessageFiles)
                    .FirstOrDefaultAsync(m => m.Id == message.Id);

                ValidateEntityExists(createdMessage, "Message", message.Id);

                var fullMessageDto = MapToDto(message: createdMessage, messageDto.SenderId, true, request);

                try
                {
                    await hubContext.Clients.Group(createdMessage.ChatId.ToString()).SendAsync("ReceiveMessageDTO", fullMessageDto);
                }
                catch (Exception hubEx)
                {
                    _logger.LogWarning(hubEx, "Failed to send message via SignalR");
                }

                return fullMessageDto;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "creating message");
                throw;
            }
        }

        public async Task<PagedMessagesDTO> GetChatMessagesAsync(int chatId, int? userId, int page, int pageSize, HttpRequest request)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 50;
                if (pageSize > 100) pageSize = 100;

                System.Diagnostics.Debug.WriteLine($"[MessageService] GetChatMessagesAsync for chatId={chatId}, page={page}, pageSize={pageSize}");

                var query = _context.Messages.Where(m => m.ChatId == chatId).
                    Include(m => m.Sender).Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes).
                    Include(m => m.MessageFiles).
                    OrderByDescending(m => m.CreatedAt).AsNoTracking();

                var totalCount = await query.CountAsync();
                var skipCount = (page - 1) * pageSize;
                var hasMoreMessages = totalCount > skipCount + pageSize;

                var messages = await query.Skip(skipCount).Take(pageSize).ToListAsync();

                System.Diagnostics.Debug.WriteLine($"[MessageService] Loaded {messages.Count} messages from DB with Sender data");

                foreach (var msg in messages)
                {
                    var senderName = msg.Sender?.Username ?? "[NO_SENDER]";
                    System.Diagnostics.Debug.WriteLine($"[MessageService] Message {msg.Id}: SenderId={msg.SenderId}, Sender={senderName}");
                }

                var messageDtos = messages.Select(m => MapToDto(m, userId, userId.HasValue && m.SenderId == userId, request)).Reverse().ToList();

                System.Diagnostics.Debug.WriteLine($"[MessageService] Converted {messageDtos.Count} messages to DTO");

                return new PagedMessagesDTO
                {
                    Messages = messageDtos,
                    CurrentPage = page,
                    TotalCount = totalCount,
                    HasMoreMessages = hasMoreMessages
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MessageService] ERROR: {ex.Message}");
                LogOperationError(ex, "getting chat messages", chatId);
                throw;
            }
        }

        private static MessageDTO MapToDto(Message message, int? userId, bool isOwn = false, HttpRequest? request = null)
        {
            var dto = new MessageDTO
            {
                Id = message.Id,
                ChatId = message.ChatId,
                SenderId = message.SenderId,
                Content = message.Content,
                CreatedAt = message.CreatedAt,
                SenderName = message.Sender?.DisplayName ?? message.Sender?.Username,
                IsOwn = isOwn
            };

            if (message.Sender != null && !string.IsNullOrEmpty(message.Sender.Avatar) && request != null)
                dto.SenderAvatarUrl = $"{request.Scheme}://{request.Host}{message.Sender.Avatar}";

            System.Diagnostics.Debug.WriteLine($"[MessageService] MapToDto Message {dto.Id}: SenderId={message.SenderId}, SenderName='{dto.SenderName}', SenderAvatarUrl='{dto.SenderAvatarUrl}'");

            dto.Files = message.MessageFiles?.Select(f => new MessageFileDTO
            {
                Id = f.Id,
                MessageId = f.MessageId,
                FileName = f.FileName,
                ContentType = f.ContentType,
                Url = f.Path != null && request != null ? ($"{request.Scheme}://{request.Host}{f.Path}") : f.Path,
                PreviewType = GetPreviewType(f.ContentType)
            }).ToList() ?? [];

            var poll = message.Polls?.FirstOrDefault();
            if (poll != null && poll.Id > 0 && !string.IsNullOrEmpty(poll.Question))
            {
                dto.Poll = new PollDTO
                {
                    Id = poll.Id,
                    MessageId = poll.MessageId,
                    CreatedById = poll.CreatedById,
                    ChatId = message.ChatId,
                    Question = poll.Question,
                    IsAnonymous = poll.IsAnonymous ?? false,
                    AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
                    CreatedAt = poll.CreatedAt,
                    ClosesAt = poll.ClosesAt,
                    Options = poll.PollOptions?.OrderBy(o => o.Position).Select(o => new PollOptionDTO
                    {
                        Id = o.Id, PollId = o.PollId, Text = o.OptionText, Position = o.Position, VotesCount = o.PollVotes?.Count ?? 0}).ToList() ?? []
                };

                if (userId.HasValue)
                {
                    dto.Poll.SelectedOptionIds = poll.PollOptions?.SelectMany(o => o.PollVotes ?? []).
                        Where(v => v.UserId == userId).Select(v => v.OptionId).ToList() ?? [];

                    dto.Poll.CanVote = dto.Poll.SelectedOptionIds.Count == 0;
                }
            }

            return dto;
        }

        private static string GetPreviewType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return "file";
            var t = contentType.ToLowerInvariant();
            if (t.StartsWith("image/")) return "image";
            if (t.StartsWith("video/")) return "video";
            if (t.StartsWith("audio/")) return "audio";
            return "file";
        }
    }
}