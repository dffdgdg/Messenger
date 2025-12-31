using MessengerAPI.Configuration;
using MessengerAPI.Helpers;
using MessengerAPI.Hubs;
using MessengerAPI.Model;
using MessengerShared.DTO;
using MessengerShared.Enum;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerAPI.Services
{
    public interface IMessageService
    {
        Task<MessageDTO> CreateMessageAsync(MessageDTO dto, HttpRequest request);
        Task<MessageDTO> UpdateMessageAsync(int messageId, int userId, UpdateMessageDTO dto, HttpRequest request);
        Task DeleteMessageAsync(int messageId, int userId);
        Task<PagedMessagesDTO> GetChatMessagesAsync(int chatId, int? userId, int page, int pageSize, HttpRequest request);
        Task<PagedMessagesDTO> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count, HttpRequest request);
        Task<PagedMessagesDTO> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count, HttpRequest request);
        Task<PagedMessagesDTO> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count, HttpRequest request);
        Task<SearchMessagesResponseDTO> SearchMessagesAsync(int chatId, int? userId, string query, int page, int pageSize, HttpRequest request);
        Task<GlobalSearchResponseDTO> GlobalSearchAsync(int userId, string query, int page, int pageSize, HttpRequest request);
        Task<bool> IsMessageOwnerAsync(int messageId, int userId);
    }

    public class MessageService(
        MessengerDbContext context,
        IHubContext<ChatHub> hubContext,
        INotificationService notificationService,
        IReadReceiptService readReceiptService,
        IOptions<MessengerSettings> settings,
        ILogger<MessageService> logger) : BaseService<MessageService>(context, logger), IMessageService
    {
        private readonly MessengerSettings _settings = settings.Value;

        public async Task<bool> IsMessageOwnerAsync(int messageId, int userId)
            => await _context.Messages.AnyAsync(m => m.Id == messageId && m.SenderId == userId);

        public async Task<MessageDTO> CreateMessageAsync(MessageDTO dto, HttpRequest request)
        {
            var message = new Message
            {
                ChatId = dto.ChatId,
                SenderId = dto.SenderId,
                Content = dto.Content,
                IsDeleted = false
            };

            if (dto.Poll != null)
            {
                message.Polls.Add(CreatePollFromDto(dto.Poll));
            }

            _context.Messages.Add(message);
            await SaveChangesAsync();

            if (dto.Files?.Count > 0)
            {
                await SaveMessageFilesAsync(message.Id, dto.Files, request);
            }

            await UpdateChatLastMessageTimeAsync(dto.ChatId);

            var createdMessage = await LoadFullMessageAsync(message.Id);
            var messageDto = createdMessage.ToDto(dto.SenderId, request);

            // Отправляем через SignalR
            await SendToGroupSafeAsync(dto.ChatId, "ReceiveMessageDTO", messageDto);

            // Обновляем счётчики непрочитанных для всех участников
            await UpdateUnreadCountsForChatAsync(dto.ChatId, dto.SenderId);

            // Отправляем уведомления
            await NotifyNewMessageSafeAsync(messageDto, request);

            _logger.LogInformation("Сообщение {MessageId} создано в чате {ChatId}", message.Id, dto.ChatId);

            return messageDto;
        }

        /// <summary>
        /// Обновить счётчики непрочитанных для всех участников чата (кроме отправителя)
        /// </summary>
        private async Task UpdateUnreadCountsForChatAsync(int chatId, int senderId)
        {
            try
            {
                var memberIds = await _context.ChatMembers
                    .Where(cm => cm.ChatId == chatId && cm.UserId != senderId)
                    .Select(cm => cm.UserId)
                    .ToListAsync();

                foreach (var memberId in memberIds)
                {
                    var unreadCount = await readReceiptService.GetUnreadCountAsync(memberId, chatId);

                    await hubContext.Clients.Group($"user_{memberId}")
                        .SendAsync("UnreadCountUpdated", chatId, unreadCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось обновить счётчики непрочитанных для чата {ChatId}", chatId);
            }
        }

        #region Get Messages Methods

        public async Task<PagedMessagesDTO> GetChatMessagesAsync(int chatId, int? userId, int page, int pageSize, HttpRequest request)
        {
            var (normalizedPage, normalizedPageSize) = NormalizePagination(
                page, _settings.DefaultPageSize, _settings.MaxPageSize);

            var query = _context.Messages
                .Where(m => m.ChatId == chatId && m.IsDeleted != true)
                .Include(m => m.Sender)
                .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                .Include(m => m.MessageFiles)
                .OrderByDescending(m => m.CreatedAt)
                .AsNoTracking();

            var totalCount = await query.CountAsync();
            var skipCount = (normalizedPage - 1) * normalizedPageSize;

            var messages = await Paginate(query, normalizedPage, normalizedPageSize).ToListAsync();

            var messageDtos = messages.Select(m => m.ToDto(userId, request)).Reverse().ToList();

            return new PagedMessagesDTO
            {
                Messages = messageDtos,
                CurrentPage = normalizedPage,
                TotalCount = totalCount,
                HasMoreMessages = totalCount > skipCount + normalizedPageSize,
                HasNewerMessages = false // При загрузке последних - новее нет
            };
        }

        /// <summary>
        /// Получить сообщения вокруг указанного ID (для скролла к непрочитанным)
        /// </summary>
        public async Task<PagedMessagesDTO> GetMessagesAroundAsync(
            int chatId, int messageId, int userId, int count, HttpRequest request)
        {
            var halfCount = count / 2;

            // Сообщения ДО целевого (включая целевое)
            var beforeAndTarget = await _context.Messages
                .Where(m => m.ChatId == chatId && m.Id <= messageId && m.IsDeleted != true)
                .OrderByDescending(m => m.Id)
                .Take(halfCount + 1)
                .Include(m => m.Sender)
                .Include(m => m.MessageFiles)
                .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                .AsNoTracking()
                .ToListAsync();

            // Сообщения ПОСЛЕ целевого
            var after = await _context.Messages
                .Where(m => m.ChatId == chatId && m.Id > messageId && m.IsDeleted != true)
                .OrderBy(m => m.Id)
                .Take(halfCount)
                .Include(m => m.Sender)
                .Include(m => m.MessageFiles)
                .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                .AsNoTracking()
                .ToListAsync();

            // Собираем в правильном порядке
            var messages = beforeAndTarget
                .OrderBy(m => m.Id)
                .Concat(after)
                .Select(m => m.ToDto(userId, request))
                .ToList();

            // Проверяем есть ли ещё старые
            var oldestLoadedId = beforeAndTarget.Count > 0 ? beforeAndTarget.Min(m => m.Id) : messageId;
            var hasOlder = await _context.Messages
                .AnyAsync(m => m.ChatId == chatId && m.Id < oldestLoadedId && m.IsDeleted != true);

            // Проверяем есть ли ещё новые
            var newestLoadedId = after.Count > 0 ? after.Max(m => m.Id) : messageId;
            var hasNewer = await _context.Messages
                .AnyAsync(m => m.ChatId == chatId && m.Id > newestLoadedId && m.IsDeleted != true);

            return new PagedMessagesDTO
            {
                Messages = messages,
                HasMoreMessages = hasOlder,
                HasNewerMessages = hasNewer,
                TotalCount = messages.Count,
                CurrentPage = 1
            };
        }

        /// <summary>
        /// Получить сообщения до указанного ID (для подгрузки старых)
        /// </summary>
        public async Task<PagedMessagesDTO> GetMessagesBeforeAsync(
            int chatId, int messageId, int userId, int count, HttpRequest request)
        {
            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId && m.Id < messageId && m.IsDeleted != true)
                .OrderByDescending(m => m.Id)
                .Take(count)
                .Include(m => m.Sender)
                .Include(m => m.MessageFiles)
                .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                .AsNoTracking()
                .ToListAsync();

            var oldestId = messages.Count > 0 ? messages.Min(m => m.Id) : messageId;
            var hasMore = await _context.Messages
                .AnyAsync(m => m.ChatId == chatId && m.Id < oldestId && m.IsDeleted != true);

            return new PagedMessagesDTO
            {
                Messages = [.. messages.OrderBy(m => m.Id).Select(m => m.ToDto(userId, request))],
                HasMoreMessages = hasMore,
                HasNewerMessages = true,
                TotalCount = messages.Count,
                CurrentPage = 1
            };
        }

        /// <summary>
        /// Получить сообщения после указанного ID (для подгрузки новых)
        /// </summary>
        public async Task<PagedMessagesDTO> GetMessagesAfterAsync(
            int chatId, int messageId, int userId, int count, HttpRequest request)
        {
            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId && m.Id > messageId && m.IsDeleted != true)
                .OrderBy(m => m.Id)
                .Take(count)
                .Include(m => m.Sender)
                .Include(m => m.MessageFiles)
                .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                .AsNoTracking()
                .ToListAsync();

            var newestId = messages.Count > 0 ? messages.Max(m => m.Id) : messageId;
            var hasNewer = await _context.Messages
                .AnyAsync(m => m.ChatId == chatId && m.Id > newestId && m.IsDeleted != true);

            return new PagedMessagesDTO
            {
                Messages = [.. messages.Select(m => m.ToDto(userId, request))],
                HasMoreMessages = true,
                HasNewerMessages = hasNewer,
                TotalCount = messages.Count,
                CurrentPage = 1
            };
        }

        #endregion
        public async Task<MessageDTO> UpdateMessageAsync(int messageId, int userId, UpdateMessageDTO dto, HttpRequest request)
        {
            var message = await LoadFullMessageAsync(messageId);

            ValidateMessageModification(message, userId);

            if (message.Polls.Count != 0)
                throw new InvalidOperationException("Нельзя редактировать сообщение с опросом");

            if (string.IsNullOrWhiteSpace(dto.Content))
                throw new ArgumentException("Содержимое сообщения не может быть пустым");

            message.Content = dto.Content.Trim();
            message.EditedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await SaveChangesAsync();

            var messageDto = message.ToDto(userId, request);

            await SendToGroupSafeAsync(message.ChatId, "MessageUpdated", messageDto);

            _logger.LogInformation("Сообщение {MessageId} отредактировано", messageId);

            return messageDto;
        }

        public async Task DeleteMessageAsync(int messageId, int userId)
        {
            var message = await _context.Messages
                .Include(m => m.MessageFiles)
                .FirstOrDefaultAsync(m => m.Id == messageId)
                ?? throw new KeyNotFoundException($"Сообщение с ID {messageId} не найдено");

            ValidateMessageModification(message, userId);

            message.IsDeleted = true;
            message.Content = null;
            message.EditedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await SaveChangesAsync();

            await SendToGroupSafeAsync(message.ChatId, "MessageDeleted", new
            {
                MessageId = messageId,
                message.ChatId
            });

            _logger.LogInformation("Сообщение {MessageId} удалено", messageId);
        }

        #region Search

        public async Task<SearchMessagesResponseDTO> SearchMessagesAsync(
            int chatId, int? userId, string query, int page, int pageSize, HttpRequest request)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new SearchMessagesResponseDTO
                {
                    Messages = [],
                    TotalCount = 0,
                    CurrentPage = page
                };
            }

            var (normalizedPage, normalizedPageSize) = NormalizePagination(page, 20, _settings.MaxPageSize);
            var escapedQuery = EscapeLikePattern(query);

            var baseQuery = _context.Messages
                .Where(m => m.ChatId == chatId)
                .Where(m => m.IsDeleted != true)
                .Where(m => m.Content != null && EF.Functions.ILike(m.Content, $"%{escapedQuery}%"))
                .Include(m => m.Sender)
                .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
                .Include(m => m.MessageFiles)
                .OrderByDescending(m => m.CreatedAt)
                .AsNoTracking();

            var totalCount = await baseQuery.CountAsync();
            var messages = await Paginate(baseQuery, normalizedPage, normalizedPageSize).ToListAsync();

            return new SearchMessagesResponseDTO
            {
                Messages = [.. messages.Select(m => m.ToDto(userId, request)).Reverse()],
                TotalCount = totalCount,
                CurrentPage = normalizedPage,
                HasMoreMessages = totalCount > ((normalizedPage - 1) * normalizedPageSize) + normalizedPageSize
            };
        }

        public async Task<GlobalSearchResponseDTO> GlobalSearchAsync(
            int userId, string query, int page, int pageSize, HttpRequest request)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new GlobalSearchResponseDTO
                {
                    Chats = [],
                    Messages = [],
                    CurrentPage = page
                };
            }

            var (normalizedPage, normalizedPageSize) = NormalizePagination(page, 20, 50);
            var escapedQuery = EscapeLikePattern(query);

            var userChatIds = await _context.ChatMembers
                .Where(cm => cm.UserId == userId)
                .Select(cm => cm.ChatId)
                .ToListAsync();

            if (userChatIds.Count == 0)
            {
                return new GlobalSearchResponseDTO
                {
                    Chats = [],
                    Messages = [],
                    CurrentPage = page
                };
            }

            var foundChats = await SearchChatsAsync(userChatIds, escapedQuery, userId, request);

            var (messages, totalCount, hasMore) = await SearchMessagesGlobalAsync(
                userChatIds, escapedQuery, userId, normalizedPage, normalizedPageSize, request);

            return new GlobalSearchResponseDTO
            {
                Chats = foundChats,
                Messages = messages,
                TotalChatsCount = foundChats.Count,
                TotalMessagesCount = totalCount,
                CurrentPage = normalizedPage,
                HasMoreMessages = hasMore
            };
        }

        private async Task<List<ChatDTO>> SearchChatsAsync(List<int> userChatIds, string query, int userId, HttpRequest request)
        {
            const int maxResults = 5;
            var result = new List<ChatDTO>();

            var dialogs = await _context.Chats
                .Where(c => userChatIds.Contains(c.Id))
                .Where(c => c.Type == ChatType.Contact)
                .Include(c => c.ChatMembers).ThenInclude(cm => cm.User)
                .AsNoTracking()
                .ToListAsync();

            foreach (var chat in dialogs)
            {
                var partner = chat.ChatMembers.FirstOrDefault(cm => cm.UserId != userId)?.User;
                if (partner == null) continue;

                var displayName = partner.FormatDisplayName();
                var username = partner.Username ?? "";

                if (displayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    username.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new ChatDTO
                    {
                        Id = chat.Id,
                        Name = displayName,
                        Type = chat.Type,
                        Avatar = !string.IsNullOrEmpty(partner.Avatar)
                            ? $"{request.Scheme}://{request.Host}{partner.Avatar}"
                            : null,
                        LastMessageDate = chat.LastMessageTime
                    });
                }
            }

            var groupChats = await _context.Chats
                .Where(c => userChatIds.Contains(c.Id))
                .Where(c => c.Type != ChatType.Contact)
                .Where(c => EF.Functions.ILike(c.Name ?? string.Empty, $"%{query}%"))
                .Take(maxResults)
                .AsNoTracking()
                .ToListAsync();

            result.AddRange(groupChats.Select(c => c.ToDto(request)));

            return [.. result.Take(maxResults)];
        }

        private async Task<(List<GlobalSearchMessageDTO> Messages, int TotalCount, bool HasMore)> SearchMessagesGlobalAsync(
            List<int> userChatIds, string query, int userId, int page, int pageSize, HttpRequest request)
        {
            var messagesQuery = _context.Messages
                .Where(m => userChatIds.Contains(m.ChatId))
                .Where(m => m.IsDeleted != true)
                .Where(m => m.Content != null && EF.Functions.ILike(m.Content, $"%{query}%"))
                .Include(m => m.Sender)
                .Include(m => m.Chat)
                .Include(m => m.MessageFiles)
                .OrderByDescending(m => m.CreatedAt)
                .AsNoTracking();

            var totalCount = await messagesQuery.CountAsync();
            var messages = await Paginate(messagesQuery, page, pageSize).ToListAsync();

            var dialogChatIds = messages
                .Where(m => m.Chat.Type == ChatType.Contact)
                .Select(m => m.ChatId)
                .Distinct()
                .ToList();

            var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId, request);

            var result = messages
                .ConvertAll(m => CreateGlobalSearchMessageDto(m, query, dialogPartners, request))
;

            return (result, totalCount, totalCount > ((page - 1) * pageSize) + pageSize);
        }

        private async Task<Dictionary<int, (string Name, string? Avatar)>> GetDialogPartnersAsync(
            List<int> chatIds, int userId, HttpRequest request)
        {
            if (chatIds.Count == 0) return [];

            var partners = await _context.ChatMembers
                .Where(cm => chatIds.Contains(cm.ChatId) && cm.UserId != userId)
                .Include(cm => cm.User)
                .AsNoTracking()
                .ToListAsync();

            return partners
                .Where(p => p.User != null)
                .ToDictionary(
                    p => p.ChatId,
                    p => (
                        Name: p.User!.FormatDisplayName(),
                        Avatar: !string.IsNullOrEmpty(p.User.Avatar)
                            ? $"{request.Scheme}://{request.Host}{p.User.Avatar}"
                            : null
                    ));
        }

        private static GlobalSearchMessageDTO CreateGlobalSearchMessageDto(
            Message message,
            string searchTerm,
            Dictionary<int, (string Name, string? Avatar)> dialogPartners,
            HttpRequest request)
        {
            var dto = new GlobalSearchMessageDTO
            {
                Id = message.Id,
                ChatId = message.ChatId,
                ChatType = message.Chat.Type,
                SenderId = message.SenderId,
                SenderName = message.Sender?.FormatDisplayName(),
                Content = message.Content,
                CreatedAt = message.CreatedAt,
                HighlightedContent = CreateHighlightedContent(message.Content, searchTerm),
                HasFiles = message.MessageFiles?.Any() ?? false
            };

            if (message.Chat.Type == ChatType.Contact && dialogPartners.TryGetValue(message.ChatId, out var partner))
            {
                dto.ChatName = partner.Name;
                dto.ChatAvatar = partner.Avatar;
            }
            else
            {
                dto.ChatName = message.Chat.Name;
                dto.ChatAvatar = !string.IsNullOrEmpty(message.Chat.Avatar)
                    ? $"{request.Scheme}://{request.Host}{message.Chat.Avatar}"
                    : null;
            }

            return dto;
        }

        private static string? CreateHighlightedContent(string? content, string searchTerm)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(searchTerm))
                return content;

            var index = content.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return content.Length > 100 ? content[..100] + "..." : content;

            const int contextLength = 40;
            var start = Math.Max(0, index - contextLength);
            var end = Math.Min(content.Length, index + searchTerm.Length + contextLength);

            var prefix = start > 0 ? "..." : "";
            var suffix = end < content.Length ? "..." : "";

            return prefix + content[start..end] + suffix;
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

        #endregion

        #region Private Methods

        private static Poll CreatePollFromDto(PollDTO pollDto) => new()
        {
            Question = pollDto.Question,
            IsAnonymous = pollDto.IsAnonymous,
            AllowsMultipleAnswers = pollDto.AllowsMultipleAnswers,
            PollOptions = pollDto.Options?.Select((o, index) => new PollOption
            {
                OptionText = o.Text,
                Position = o.Position > 0 ? o.Position : index
            }).ToList() ?? []
        };

        private async Task SaveMessageFilesAsync(int messageId, List<MessageFileDTO> files, HttpRequest request)
        {
            foreach (var f in files)
            {
                var path = f.Url?.Replace($"{request.Scheme}://{request.Host}", "") ?? f.Url;

                _context.MessageFiles.Add(new MessageFile
                {
                    MessageId = messageId,
                    FileName = f.FileName,
                    ContentType = f.ContentType,
                    Path = path
                });
            }

            await SaveChangesAsync();
        }

        private async Task UpdateChatLastMessageTimeAsync(int chatId)
        {
            var chat = await _context.Chats.FindAsync(chatId);
            if (chat != null)
            {
                chat.LastMessageTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                await _context.SaveChangesAsync();
            }
        }

        private async Task<Message> LoadFullMessageAsync(int messageId) => await _context.Messages
            .Include(m => m.Sender)
            .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
            .Include(m => m.MessageFiles)
            .FirstOrDefaultAsync(m => m.Id == messageId)
            ?? throw new KeyNotFoundException($"Сообщение {messageId} не найдено");

        private static void ValidateMessageModification(Message message, int userId)
        {
            if (message.SenderId != userId)
                throw new UnauthorizedAccessException("Вы можете изменять только свои сообщения");

            if (message.IsDeleted == true)
                throw new InvalidOperationException("Сообщение уже удалено");
        }

        private async Task SendToGroupSafeAsync<T>(int chatId, string method, T data)
        {
            try
            {
                await hubContext.Clients.Group($"chat_{chatId}").SendAsync(method, data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить {Method} через SignalR в чат {ChatId}", method, chatId);
            }
        }

        private async Task NotifyNewMessageSafeAsync(MessageDTO message, HttpRequest request)
        {
            try
            {
                await notificationService.NotifyNewMessageAsync(message, request);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить уведомления для сообщения {MessageId}", message.Id);
            }
        }

        #endregion
    }
}