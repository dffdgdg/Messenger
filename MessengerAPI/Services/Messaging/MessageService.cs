using MessengerAPI.Common;
using MessengerAPI.Configuration;
using MessengerAPI.Mapping;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.Search;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerAPI.Services.Messaging;

public interface IMessageService
{
    Task<Result<MessageDTO>> CreateMessageAsync(MessageDTO dto);
    Task<Result<MessageDTO>> UpdateMessageAsync(int messageId, int userId, UpdateMessageDTO dto);
    Task<Result> DeleteMessageAsync(int messageId, int userId);
    Task<Result<PagedMessagesDTO>> GetChatMessagesAsync(int chatId, int userId, int page, int pageSize);
    Task<Result<PagedMessagesDTO>> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDTO>> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDTO>> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count);
    Task<Result<SearchMessagesResponseDTO>> SearchMessagesAsync(int chatId, int userId, string query, int page, int pageSize);
    Task<Result<GlobalSearchResponseDTO>> GlobalSearchAsync(int userId, string query, int page, int pageSize);
}

public class MessageService(
    MessengerDbContext context,
    IHubNotifier hubNotifier,
    INotificationService notificationService,
    IReadReceiptService readReceiptService,
    IUrlBuilder urlBuilder,
    TranscriptionQueue transcriptionQueue,
    IOptions<MessengerSettings> settings,
    ILogger<MessageService> logger)
    : BaseService<MessageService>(context, logger), IMessageService
{
    private readonly MessengerSettings _settings = settings.Value;

    #region Base Query

    private IQueryable<Message> MessagesWithIncludes()
        => _context.Messages
            .Include(m => m.Sender)
            .Include(m => m.MessageFiles)
            .Include(m => m.Polls)
                .ThenInclude(p => p.PollOptions)
                .ThenInclude(o => o.PollVotes)
            .Include(m => m.ReplyToMessage)
                .ThenInclude(r => r!.Sender)
            .Include(m => m.ForwardedFromMessage)
                .ThenInclude(f => f!.Sender);

    #endregion

    public async Task<Result<MessageDTO>> CreateMessageAsync(MessageDTO dto)
    {
        if (dto.ReplyToMessageId.HasValue)
        {
            var replyExists = await _context.Messages.AnyAsync(m =>
                m.Id == dto.ReplyToMessageId.Value
                && m.ChatId == dto.ChatId
                && m.IsDeleted != true);

            if (!replyExists)
                return Result<MessageDTO>.Failure("Сообщение для ответа не найдено в этом чате");
        }

        if (dto.ForwardedFromMessageId.HasValue)
        {
            var originalExists = await _context.Messages.AnyAsync(m =>
                m.Id == dto.ForwardedFromMessageId.Value
                && m.IsDeleted != true);

            if (!originalExists)
                return Result<MessageDTO>.Failure("Оригинальное сообщение для пересылки не найдено");
        }

        var message = new Message
        {
            ChatId = dto.ChatId,
            SenderId = dto.SenderId,
            Content = dto.IsVoiceMessage ? null : dto.Content,
            IsDeleted = false,
            IsVoiceMessage = dto.IsVoiceMessage,
            TranscriptionStatus = dto.IsVoiceMessage ? "pending" : null,
            ReplyToMessageId = dto.ReplyToMessageId,
            ForwardedFromMessageId = dto.ForwardedFromMessageId,
        };

        _context.Messages.Add(message);
        await SaveChangesAsync();

        if (dto.Files?.Count > 0)
            await SaveMessageFilesAsync(message.Id, dto.Files);

        await UpdateChatLastMessageTimeAsync(dto.ChatId);

        var createdMessage = await MessagesWithIncludes()
            .FirstAsync(m => m.Id == message.Id);
        var messageDto = createdMessage.ToDto(dto.SenderId, urlBuilder);

        await hubNotifier.SendToChatAsync(
            dto.ChatId, "ReceiveMessageDTO", messageDto);

        await NotifyAndUpdateUnreadAsync(messageDto);

        if (message.IsVoiceMessage)
        {
            await transcriptionQueue.EnqueueAsync(message.Id);
            _logger.LogDebug(
                "Голосовое {MessageId} → очередь транскрибации", message.Id);
        }

        _logger.LogInformation(
            "Сообщение {MessageId} создано в чате {ChatId}",
            message.Id, dto.ChatId);

        return Result<MessageDTO>.Success(messageDto);
    }

    #region Get Messages

    public async Task<Result<PagedMessagesDTO>> GetChatMessagesAsync(
        int chatId, int userId, int page, int pageSize)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePagination(
            page, pageSize, _settings.MaxPageSize);

        var query = MessagesWithIncludes()
            .Where(m => m.ChatId == chatId && m.IsDeleted != true)
            .OrderByDescending(m => m.CreatedAt)
            .AsNoTracking();

        var totalCount = await query.CountAsync();
        var skipCount = (normalizedPage - 1) * normalizedPageSize;

        var messages = await Paginate(query, normalizedPage, normalizedPageSize)
            .ToListAsync();

        var messageDtos = messages
            .Select(m => m.ToDto(userId, urlBuilder))
            .Reverse()
            .ToList();

        return Result<PagedMessagesDTO>.Success(new PagedMessagesDTO
        {
            Messages = messageDtos,
            CurrentPage = normalizedPage,
            TotalCount = totalCount,
            HasMoreMessages = totalCount > skipCount + normalizedPageSize,
            HasNewerMessages = false
        });
    }

    public async Task<Result<PagedMessagesDTO>> GetMessagesAroundAsync(
        int chatId, int messageId, int userId, int count)
    {
        var halfCount = count / 2;

        var beforeAndTarget = await MessagesWithIncludes()
            .Where(m => m.ChatId == chatId
                && m.Id <= messageId
                && m.IsDeleted != true)
            .OrderByDescending(m => m.Id)
            .Take(halfCount + 1)
            .AsNoTracking()
            .ToListAsync();

        var after = await MessagesWithIncludes()
            .Where(m => m.ChatId == chatId
                && m.Id > messageId
                && m.IsDeleted != true)
            .OrderBy(m => m.Id)
            .Take(halfCount)
            .AsNoTracking()
            .ToListAsync();

        var messages = beforeAndTarget
            .OrderBy(m => m.Id)
            .Concat(after)
            .Select(m => m.ToDto(userId, urlBuilder))
            .ToList();

        var oldestLoadedId = beforeAndTarget.Count > 0
            ? beforeAndTarget.Min(m => m.Id) : messageId;
        var hasOlder = await _context.Messages.AnyAsync(m =>
            m.ChatId == chatId
            && m.Id < oldestLoadedId
            && m.IsDeleted != true);

        var newestLoadedId = after.Count > 0
            ? after.Max(m => m.Id) : messageId;
        var hasNewer = await _context.Messages.AnyAsync(m =>
            m.ChatId == chatId
            && m.Id > newestLoadedId
            && m.IsDeleted != true);

        return Result<PagedMessagesDTO>.Success(new PagedMessagesDTO
        {
            Messages = messages,
            HasMoreMessages = hasOlder,
            HasNewerMessages = hasNewer,
            TotalCount = messages.Count,
            CurrentPage = 1
        });
    }

    public async Task<Result<PagedMessagesDTO>> GetMessagesBeforeAsync(
        int chatId, int messageId, int userId, int count)
    {
        var messages = await MessagesWithIncludes()
            .Where(m => m.ChatId == chatId
                && m.Id < messageId
                && m.IsDeleted != true)
            .OrderByDescending(m => m.Id)
            .Take(count)
            .AsNoTracking()
            .ToListAsync();

        var oldestId = messages.Count > 0
            ? messages.Min(m => m.Id) : messageId;
        var hasMore = await _context.Messages.AnyAsync(m =>
            m.ChatId == chatId
            && m.Id < oldestId
            && m.IsDeleted != true);

        return Result<PagedMessagesDTO>.Success(new PagedMessagesDTO
        {
            Messages = [.. messages
                .OrderBy(m => m.Id)
                .Select(m => m.ToDto(userId, urlBuilder))],
            HasMoreMessages = hasMore,
            HasNewerMessages = true,
            TotalCount = messages.Count,
            CurrentPage = 1
        });
    }

    public async Task<Result<PagedMessagesDTO>> GetMessagesAfterAsync(
        int chatId, int messageId, int userId, int count)
    {
        var messages = await MessagesWithIncludes()
            .Where(m => m.ChatId == chatId
                && m.Id > messageId
                && m.IsDeleted != true)
            .OrderBy(m => m.Id)
            .Take(count)
            .AsNoTracking()
            .ToListAsync();

        var newestId = messages.Count > 0
            ? messages.Max(m => m.Id) : messageId;
        var hasNewer = await _context.Messages.AnyAsync(m =>
            m.ChatId == chatId
            && m.Id > newestId
            && m.IsDeleted != true);

        return Result<PagedMessagesDTO>.Success(new PagedMessagesDTO
        {
            Messages = [.. messages
                .Select(m => m.ToDto(userId, urlBuilder))],
            HasMoreMessages = true,
            HasNewerMessages = hasNewer,
            TotalCount = messages.Count,
            CurrentPage = 1
        });
    }

    #endregion

    public async Task<Result<MessageDTO>> UpdateMessageAsync(
        int messageId, int userId, UpdateMessageDTO dto)
    {
        var message = await MessagesWithIncludes()
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message is null)
            return Result<MessageDTO>.Failure($"Сообщение {messageId} не найдено");

        if (message.SenderId != userId)
            return Result<MessageDTO>.Failure("Вы можете изменять только свои сообщения");

        if (message.IsDeleted == true)
            return Result<MessageDTO>.Failure("Сообщение уже удалено");

        if (message.Polls.Count != 0)
            return Result<MessageDTO>.Failure("Нельзя редактировать сообщение с опросом");

        if (message.IsVoiceMessage)
            return Result<MessageDTO>.Failure("Нельзя редактировать голосовое сообщение");

        if (message.ForwardedFromMessageId.HasValue)
            return Result<MessageDTO>.Failure("Нельзя редактировать пересланное сообщение");

        if (string.IsNullOrWhiteSpace(dto.Content))
            return Result<MessageDTO>.Failure("Содержимое сообщения не может быть пустым");

        message.Content = dto.Content.Trim();
        message.EditedAt = AppDateTime.UtcNow;
        await SaveChangesAsync();

        var messageDto = message.ToDto(userId, urlBuilder);
        await hubNotifier.SendToChatAsync(
            message.ChatId, "MessageUpdated", messageDto);

        _logger.LogInformation("Сообщение {MessageId} отредактировано", messageId);
        return Result<MessageDTO>.Success(messageDto);
    }

    public async Task<Result> DeleteMessageAsync(int messageId, int userId)
    {
        var message = await _context.Messages
            .Include(m => m.MessageFiles)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message is null)
            return Result.Failure($"Сообщение с ID {messageId} не найдено");

        if (message.SenderId != userId)
            return Result.Failure("Вы можете удалять только свои сообщения");

        if (message.IsDeleted == true)
            return Result.Failure("Сообщение уже удалено");

        message.IsDeleted = true;
        message.Content = null;
        message.EditedAt = AppDateTime.UtcNow;
        await SaveChangesAsync();

        await hubNotifier.SendToChatAsync(
            message.ChatId, "MessageDeleted",
            new { MessageId = messageId, message.ChatId });

        _logger.LogInformation("Сообщение {MessageId} удалено", messageId);
        return Result.Success();
    }

    #region Search

    public async Task<Result<SearchMessagesResponseDTO>> SearchMessagesAsync(
        int chatId, int userId, string query, int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<SearchMessagesResponseDTO>.Success(
                new SearchMessagesResponseDTO
                {
                    Messages = [],
                    TotalCount = 0,
                    CurrentPage = page
                });
        }

        var (normalizedPage, normalizedPageSize) = NormalizePagination(
            page, 20, _settings.MaxPageSize);
        var escapedQuery = EscapeLikePattern(query);

        var baseQuery = MessagesWithIncludes()
            .Where(m => m.ChatId == chatId)
            .Where(m => m.IsDeleted != true)
            .Where(m => m.Content != null
                && EF.Functions.ILike(m.Content, $"%{escapedQuery}%"))
            .OrderByDescending(m => m.CreatedAt)
            .AsNoTracking();

        var totalCount = await baseQuery.CountAsync();
        var messages = await Paginate(baseQuery, normalizedPage, normalizedPageSize)
            .ToListAsync();

        return Result<SearchMessagesResponseDTO>.Success(
            new SearchMessagesResponseDTO
            {
                Messages = [.. messages
                    .Select(m => m.ToDto(userId, urlBuilder))
                    .Reverse()],
                TotalCount = totalCount,
                CurrentPage = normalizedPage,
                HasMoreMessages = totalCount
                    > ((normalizedPage - 1) * normalizedPageSize) + normalizedPageSize
            });
    }

    public async Task<Result<GlobalSearchResponseDTO>> GlobalSearchAsync(
        int userId, string query, int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<GlobalSearchResponseDTO>.Success(
                new GlobalSearchResponseDTO
                {
                    Chats = [],
                    Messages = [],
                    CurrentPage = page
                });
        }

        var (normalizedPage, normalizedPageSize) = NormalizePagination(
            page, 20, 50);
        var escapedQuery = EscapeLikePattern(query);

        var userChatIds = await _context.ChatMembers
            .Where(cm => cm.UserId == userId)
            .Select(cm => cm.ChatId)
            .ToListAsync();

        if (userChatIds.Count == 0)
        {
            return Result<GlobalSearchResponseDTO>.Success(
                new GlobalSearchResponseDTO
                {
                    Chats = [],
                    Messages = [],
                    CurrentPage = page
                });
        }

        var foundChats = await SearchChatsAsync(
            userChatIds, escapedQuery, userId);

        var (messages, totalCount, hasMore) = await SearchMessagesGlobalAsync(
            userChatIds, escapedQuery, userId, normalizedPage, normalizedPageSize);

        return Result<GlobalSearchResponseDTO>.Success(
            new GlobalSearchResponseDTO
            {
                Chats = foundChats,
                Messages = messages,
                TotalChatsCount = foundChats.Count,
                TotalMessagesCount = totalCount,
                CurrentPage = normalizedPage,
                HasMoreMessages = hasMore
            });
    }

    private async Task<List<ChatDTO>> SearchChatsAsync(
        List<int> userChatIds, string query, int userId)
    {
        const int maxResults = 5;
        var result = new List<ChatDTO>();

        var dialogs = await _context.Chats
            .Where(c => userChatIds.Contains(c.Id))
            .Where(c => c.Type == ChatType.Contact)
            .Include(c => c.ChatMembers)
                .ThenInclude(cm => cm.User)
            .AsNoTracking()
            .ToListAsync();

        foreach (var chat in dialogs)
        {
            var partner = chat.ChatMembers
                .FirstOrDefault(cm => cm.UserId != userId)?.User;
            if (partner is null) continue;

            var displayName = partner.FormatDisplayName();
            var username = partner.Username ?? "";

            if (displayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || username.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ChatDTO
                {
                    Id = chat.Id,
                    Name = displayName,
                    Type = chat.Type,
                    Avatar = urlBuilder.BuildUrl(partner.Avatar),
                    LastMessageDate = chat.LastMessageTime
                });
            }
        }

        var groupChats = await _context.Chats
            .Where(c => userChatIds.Contains(c.Id))
            .Where(c => c.Type != ChatType.Contact)
            .Where(c => EF.Functions.ILike(
                c.Name ?? string.Empty, $"%{query}%"))
            .Take(maxResults)
            .AsNoTracking()
            .ToListAsync();

        result.AddRange(groupChats.Select(c => c.ToDto(urlBuilder)));

        return [.. result.Take(maxResults)];
    }

    private async Task<(List<GlobalSearchMessageDTO> Messages, int TotalCount, bool HasMore)>
        SearchMessagesGlobalAsync(
            List<int> userChatIds, string query, int userId, int page, int pageSize)
    {
        var messagesQuery = _context.Messages
            .Where(m => userChatIds.Contains(m.ChatId))
            .Where(m => m.IsDeleted != true)
            .Where(m => m.Content != null
                && EF.Functions.ILike(m.Content, $"%{query}%"))
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

        var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId);

        var result = messages.ConvertAll(m =>
            CreateGlobalSearchMessageDto(m, query, dialogPartners));

        return (result, totalCount,
            totalCount > ((page - 1) * pageSize) + pageSize);
    }

    private async Task<Dictionary<int, (string Name, string? Avatar)>>
        GetDialogPartnersAsync(List<int> chatIds, int userId)
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
                    Avatar: urlBuilder.BuildUrl(p.User.Avatar)
                ));
    }

    private GlobalSearchMessageDTO CreateGlobalSearchMessageDto(
        Message message, string searchTerm,
        Dictionary<int, (string Name, string? Avatar)> dialogPartners)
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
            HighlightedContent = CreateHighlightedContent(
                message.Content, searchTerm),
            HasFiles = message.MessageFiles?.Count > 0
        };

        if (message.Chat.Type == ChatType.Contact
            && dialogPartners.TryGetValue(message.ChatId, out var partner))
        {
            dto.ChatName = partner.Name;
            dto.ChatAvatar = partner.Avatar;
        }
        else
        {
            dto.ChatName = message.Chat.Name;
            dto.ChatAvatar = urlBuilder.BuildUrl(message.Chat.Avatar);
        }

        return dto;
    }

    private static string? CreateHighlightedContent(
        string? content, string searchTerm)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(searchTerm))
            return content;

        var index = content.IndexOf(
            searchTerm, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return content.Length > 100 ? content[..100] + "..." : content;

        const int contextLength = 40;
        var start = Math.Max(0, index - contextLength);
        var end = Math.Min(
            content.Length, index + searchTerm.Length + contextLength);

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

    private async Task SaveMessageFilesAsync(
        int messageId, List<MessageFileDTO> files)
    {
        foreach (var f in files)
        {
            var path = f.Url;
            var baseUrl = urlBuilder.BuildUrl("/");
            if (baseUrl != null && path?.StartsWith(baseUrl) == true)
                path = path[baseUrl.Length..];

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
            chat.LastMessageTime = AppDateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    private async Task NotifyAndUpdateUnreadAsync(MessageDTO message)
    {
        try
        {
            var usersToNotify = await _context.ChatMembers
                .Where(cm => cm.ChatId == message.ChatId
                    && cm.UserId != message.SenderId)
                .Select(cm => new
                {
                    cm.UserId,
                    cm.NotificationsEnabled,
                    GlobalEnabled = cm.User.UserSetting == null
                        || cm.User.UserSetting.NotificationsEnabled
                })
                .ToListAsync();

            foreach (var member in usersToNotify)
            {
                var unreadCount = await readReceiptService.GetUnreadCountAsync(member.UserId, message.ChatId);

                await hubNotifier.SendToUserAsync(member.UserId, "UnreadCountUpdated",
                    new { message.ChatId, UnreadCount = unreadCount });

                if (member.NotificationsEnabled && member.GlobalEnabled)
                {
                    await notificationService.SendNotificationAsync(member.UserId, message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка уведомлений для чата {ChatId}", message.ChatId);
        }
    }

    #endregion
}