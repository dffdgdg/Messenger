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
        Task<MessageDTO> UpdateMessageAsync(int messageId, int userId, UpdateMessageDTO updateDto, HttpRequest request);
        Task DeleteMessageAsync(int messageId, int userId);
        Task<PagedMessagesDTO> GetChatMessagesAsync(int chatId, int? userId, int page, int pageSize, HttpRequest request);
        Task<SearchMessagesResponseDTO> SearchMessagesAsync(int chatId, int? userId, string query, int page, int pageSize, HttpRequest request);
        Task<bool> IsMessageOwnerAsync(int messageId, int userId);
    }

    public class MessageService(
        MessengerDbContext context,
        IHubContext<ChatHub> hubContext,
        ILogger<MessageService> logger)
        : BaseService<MessageService>(context, logger), IMessageService
    {
        /// <summary>
        /// Проверяет, является ли пользователь автором сообщения
        /// </summary>
        public async Task<bool> IsMessageOwnerAsync(int messageId, int userId)
        {
            return await _context.Messages
                .AnyAsync(m => m.Id == messageId && m.SenderId == userId);
        }

        /// <summary>
        /// Редактирование сообщения (только автор может редактировать)
        /// </summary>
        public async Task<MessageDTO> UpdateMessageAsync(int messageId, int userId, UpdateMessageDTO updateDto, HttpRequest request)
        {
            try
            {
                var message = await _context.Messages
                    .Include(m => m.Sender)
                    .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                    .Include(m => m.MessageFiles)
                    .FirstOrDefaultAsync(m => m.Id == messageId)
                    ?? throw new KeyNotFoundException($"Сообщение с ID {messageId} не найдено");

                if (message.SenderId != userId)
                    throw new UnauthorizedAccessException("Вы можете редактировать только свои сообщения");

                if (message.IsDeleted == true)
                    throw new InvalidOperationException("Нельзя редактировать удалённое сообщение");

                if (message.Polls.Count != 0)
                    throw new InvalidOperationException("Нельзя редактировать сообщение с опросом");

                if (string.IsNullOrWhiteSpace(updateDto.Content))
                    throw new ArgumentException("Содержимое сообщения не может быть пустым");

                message.Content = updateDto.Content.Trim();
                message.EditedAt = DateTime.Now;

                await SaveChangesAsync();

                _logger.LogInformation("Сообщение {MessageId} отредактировано пользователем {UserId}",
                    messageId, userId);

                var messageDto = MapToDto(message, userId, true, request);

                try
                {
                    await hubContext.Clients.Group(message.ChatId.ToString()).SendAsync("MessageUpdated", messageDto);
                }
                catch (Exception hubEx)
                {
                    _logger.LogWarning(hubEx, "Не удалось отправить обновление сообщения через SignalR");
                }

                return messageDto;
            }
            catch (Exception ex) when (ex is not KeyNotFoundException
                                       && ex is not UnauthorizedAccessException
                                       && ex is not InvalidOperationException
                                       && ex is not ArgumentException)
            {
                LogOperationError(ex, "редактирование сообщения", messageId);
                throw;
            }
        }

        /// <summary>
        /// Удаление сообщения
        /// </summary>
        public async Task DeleteMessageAsync(int messageId, int userId)
        {
            try
            {
                var message = await _context.Messages
                    .Include(m => m.MessageFiles)
                    .FirstOrDefaultAsync(m => m.Id == messageId)
                    ?? throw new KeyNotFoundException($"Сообщение с ID {messageId} не найдено");

                if (message.SenderId != userId)
                    throw new UnauthorizedAccessException("Вы можете удалять только свои сообщения");

                if (message.IsDeleted == true)
                    throw new InvalidOperationException("Сообщение уже удалено");

                message.IsDeleted = true;
                message.Content = null;
                message.EditedAt = DateTime.Now;

                await SaveChangesAsync();

                _logger.LogInformation("Сообщение {MessageId} удалено пользователем {UserId}",
                    messageId, userId);

                try
                {
                    await hubContext.Clients.Group(message.ChatId.ToString()).SendAsync("MessageDeleted", new { MessageId = messageId, message.ChatId });
                }
                catch (Exception hubEx)
                {
                    _logger.LogWarning(hubEx, "Не удалось отправить уведомление об удалении через SignalR");
                }
            }
            catch (Exception ex) when (ex is not KeyNotFoundException
                                       && ex is not UnauthorizedAccessException
                                       && ex is not InvalidOperationException)
            {
                LogOperationError(ex, "удаление сообщения", messageId);
                throw;
            }
        }

        public async Task<MessageDTO> CreateMessageAsync(MessageDTO messageDto, HttpRequest request)
        {
            try
            {
                var message = new Message
                {
                    ChatId = messageDto.ChatId,
                    SenderId = messageDto.SenderId,
                    Content = messageDto.Content,
                    IsDeleted = false
                };

                if (messageDto.Poll != null)
                {
                    var poll = new Poll
                    {
                        Question = messageDto.Poll.Question,
                        IsAnonymous = messageDto.Poll.IsAnonymous,
                        AllowsMultipleAnswers = messageDto.Poll.AllowsMultipleAnswers,
                        PollOptions = messageDto.Poll.Options?.Select(o => new PollOption
                        {
                            OptionText = o.Text,
                            Position = o.Position
                        }).ToList() ?? []
                    };

                    message.Polls.Add(poll);
                }

                _context.Messages.Add(message);
                await SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"[MessageService] Message created with ID: {message.Id}");

                if (messageDto.Files != null && messageDto.Files.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[MessageService] Processing {messageDto.Files.Count} files for message {message.Id}");

                    foreach (var f in messageDto.Files)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[MessageService] Creating MessageFile: {f.FileName}");

                            var mf = new MessageFile
                            {
                                FileName = f.FileName,
                                ContentType = f.ContentType,
                                Path = f.Url?.Replace($"{request.Scheme}://{request.Host}", "") ?? f.Url,
                                MessageId = message.Id
                            };
                            _context.MessageFiles.Add(mf);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MessageService] Error processing file {f.FileName}: {ex.Message}");
                        }
                    }

                    if (messageDto.Files.Count > 0)
                        await SaveChangesAsync();
                }

                var createdMessage = await _context.Messages
                    .Include(m => m.Sender)
                    .Include(m => m.Polls).ThenInclude(p => p.PollOptions)
                    .Include(m => m.MessageFiles)
                    .FirstOrDefaultAsync(m => m.Id == message.Id);

                ValidateEntityExists(createdMessage, "Message", message.Id);

                System.Diagnostics.Debug.WriteLine($"[MessageService] Message {message.Id} has {createdMessage?.MessageFiles?.Count ?? 0} files");

                var fullMessageDto = MapToDto(createdMessage!, messageDto.SenderId, true, request);

                try
                {
                    await hubContext.Clients.Group(createdMessage!.ChatId.ToString())
                        .SendAsync("ReceiveMessageDTO", fullMessageDto);
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

                var query = _context.Messages
                    .Where(m => m.ChatId == chatId)
                    .Include(m => m.Sender)
                    .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                    .Include(m => m.MessageFiles)
                    .OrderByDescending(m => m.CreatedAt)
                    .AsNoTracking();

                var totalCount = await query.CountAsync();
                var skipCount = (page - 1) * pageSize;
                var hasMoreMessages = totalCount > skipCount + pageSize;

                var messages = await query.Skip(skipCount).Take(pageSize).ToListAsync();

                var messageDtos = messages.Select(m => MapToDto(m, userId, userId.HasValue && m.SenderId == userId, request)).Reverse().ToList();

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
                LogOperationError(ex, "getting chat messages", chatId);
                throw;
            }
        }

        private static MessageDTO MapToDto(Message message, int? userId, bool isOwn = false, HttpRequest? request = null, IWebHostEnvironment? env = null)
        {
            var isDeleted = message.IsDeleted ?? false;

            var dto = new MessageDTO
            {
                Id = message.Id,
                ChatId = message.ChatId,
                SenderId = message.SenderId,
                Content = isDeleted ? "[Сообщение удалено]" : message.Content,
                CreatedAt = message.CreatedAt,
                EditedAt = message.EditedAt,
                IsEdited = message.EditedAt.HasValue && !isDeleted,
                IsDeleted = isDeleted,
                SenderName = message.Sender?.DisplayName ?? message.Sender?.Username,
                IsOwn = isOwn
            };

            if (message.Sender != null && !string.IsNullOrEmpty(message.Sender.Avatar) && request != null)
                dto.SenderAvatarUrl = $"{request.Scheme}://{request.Host}{message.Sender.Avatar}";

            if (!isDeleted)
            {
                dto.Files = message.MessageFiles?.Select(f => new MessageFileDTO
                {
                    Id = f.Id,
                    MessageId = f.MessageId,
                    FileName = f.FileName,
                    ContentType = f.ContentType,
                    Url = f.Path != null && request != null ? $"{request.Scheme}://{request.Host}{f.Path}": f.Path,
                    PreviewType = GetPreviewType(f.ContentType),
                    FileSize = 0
                }).ToList() ?? [];

                var poll = message.Polls?.FirstOrDefault();
                if (poll != null && poll.Id > 0 && !string.IsNullOrEmpty(poll.Question))
                {
                    dto.Poll = new PollDTO
                    {
                        Id = poll.Id,
                        MessageId = poll.MessageId,
                        ChatId = message.ChatId,
                        Question = poll.Question,
                        IsAnonymous = poll.IsAnonymous ?? false,
                        AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
                        CreatedAt = poll.CreatedAt,
                        ClosesAt = poll.ClosesAt,
                        Options = poll.PollOptions?.OrderBy(o => o.Position).Select(o => new PollOptionDTO
                        {
                            Id = o.Id,
                            PollId = o.PollId,
                            Text = o.OptionText,
                            Position = o.Position,
                            VotesCount = o.PollVotes?.Count ?? 0
                        }).ToList() ?? []
                    };

                    if (userId.HasValue)
                    {
                        dto.Poll.SelectedOptionIds = poll.PollOptions?
                            .SelectMany(o => o.PollVotes ?? [])
                            .Where(v => v.UserId == userId)
                            .Select(v => v.OptionId)
                            .ToList() ?? [];
                        dto.Poll.CanVote = dto.Poll.SelectedOptionIds.Count == 0;
                    }
                }
            }
            else
            {
                dto.Files = [];
            }

            return dto;
        }

        private static long GetFileSize(string? relativePath, IWebHostEnvironment env)
        {
            if (string.IsNullOrEmpty(relativePath))
                return 0;

            try
            {
                var fullPath = Path.Combine(env.WebRootPath ?? "wwwroot", relativePath.TrimStart('/'));
                if (File.Exists(fullPath))
                {
                    return new FileInfo(fullPath).Length;
                }
            }
            catch { }

            return 0;
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

        public async Task<SearchMessagesResponseDTO> SearchMessagesAsync(
            int chatId, int? userId, string query, int page, int pageSize, HttpRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return new SearchMessagesResponseDTO
                    {
                        Messages = [],
                        TotalCount = 0,
                        CurrentPage = page
                    };

                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 20;
                if (pageSize > 100) pageSize = 100;

                var escapedQuery = EscapeLikePattern(query);

                var baseQuery = _context.Messages
                    .Where(m => m.ChatId == chatId &&
                                (m.IsDeleted == null || m.IsDeleted == false) &&
                                m.Content != null &&
                                EF.Functions.ILike(m.Content, $"%{escapedQuery}%"))
                    .Include(m => m.Sender)
                    .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                    .Include(m => m.MessageFiles)
                    .OrderByDescending(m => m.CreatedAt)
                    .AsNoTracking();

                var totalCount = await baseQuery.CountAsync();
                var skipCount = (page - 1) * pageSize;
                var hasMoreMessages = totalCount > skipCount + pageSize;

                var messages = await baseQuery
                    .Skip(skipCount)
                    .Take(pageSize)
                    .ToListAsync();

                var messageDtos = messages
                    .Select(m => MapToDto(m, userId, userId.HasValue && m.SenderId == userId, request))
                    .Reverse()
                    .ToList();

                return new SearchMessagesResponseDTO
                {
                    Messages = messageDtos,
                    TotalCount = totalCount,
                    CurrentPage = page,
                    HasMoreMessages = hasMoreMessages
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages in chat {ChatId}", chatId);
                throw;
            }
        }

        private static string EscapeLikePattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return pattern;

            return pattern
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
        }
    }
}