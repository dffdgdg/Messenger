using MessengerAPI.Services.Base;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.ReadReceipt;
using MessengerShared.DTO.Message;

namespace MessengerAPI.Services.Messaging;

public interface IMessageService
{
    Task<Result<MessageDto>> CreateMessageAsync(int senderId, CreateMessageRequest request);
    Task<Result<MessageDto>> UpdateMessageAsync(int messageId, int userId, UpdateMessageDto dto);
    Task<Result> DeleteMessageAsync(int messageId, int userId);
    Task<Result<PagedMessagesDto>> GetChatMessagesAsync(int chatId, int userId, int page, int pageSize);
    Task<Result<PagedMessagesDto>> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDto>> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDto>> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count);
    Task<Result<SearchMessagesResponseDto>> SearchMessagesAsync(int chatId, int userId, string query, int page, int pageSize);
    Task<Result<GlobalSearchResponseDto>> GlobalSearchAsync(int userId, string query, int page, int pageSize);
}
public class MessageService(
    MessengerDbContext context,
    IAccessControlService accessControl,
    IHubNotifier hubNotifier,
    INotificationService notificationService,
    IReadReceiptService readReceiptService,
    IUrlBuilder urlBuilder,
    IFileService fileService,
    TranscriptionQueue transcriptionQueue,
    IOptions<MessengerSettings> settings,
    AppDateTime appDateTime,
    ILogger<MessageService> logger)
    : BaseService<MessageService>(context, logger), IMessageService
{
    private readonly MessengerSettings _settings = settings.Value;

    #region Base Query

    private IQueryable<Message> MessagesWithIncludes()
        => _context.Messages
            .Include(m => m.Sender)
            .Include(m => m.TargetUser)
            .Include(m => m.VoiceMessage)
            .Include(m => m.MessageFiles)
            .Include(m => m.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
            .Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender);

    #endregion

    #region Create

    public async Task<Result<MessageDto>> CreateMessageAsync(int senderId, CreateMessageRequest request)
    {
        var accessResult = await accessControl.CheckIsMemberAsync(senderId, request.ChatId);
        if (accessResult.IsFailure)
            return Result<MessageDto>.FromFailure(accessResult);

        var contentValidation = ValidateContent(request);
        if (contentValidation.IsFailure)
            return Result<MessageDto>.FromFailure(contentValidation);

        var referencesValidation = await ValidateReferencesAsync(request);
        if (referencesValidation.IsFailure)
            return Result<MessageDto>.FromFailure(referencesValidation);

        var message = BuildMessage(senderId, request);
        _context.Messages.Add(message);

        if (request.IsVoiceMessage)
        {
            var voiceResult = AttachVoiceMessage(message, request);
            if (voiceResult.IsFailure)
                return Result<MessageDto>.FromFailure(voiceResult);
        }

        AttachFiles(message, request.Files);
        await MarkChatUpdatedAsync(request.ChatId);

        var saveResult = await SaveChangesAsync();
        if (saveResult.IsFailure)
            return Result<MessageDto>.FromFailure(saveResult);

        return await PostCreateAsync(message, senderId);
    }

    private static Result ValidateContent(CreateMessageRequest request)
    {
        if (request.IsVoiceMessage)
            return Result.Success();

        if (request.ForwardedFromMessageId.HasValue)
            return Result.Success();

        if (string.IsNullOrWhiteSpace(request.Content) && request.Files is not { Count: > 0 })
            return Result.Failure("Сообщение должно содержать текст или файлы");

        return Result.Success();
    }

    private async Task<Result> ValidateReferencesAsync(CreateMessageRequest request)
    {
        if (request.ReplyToMessageId.HasValue)
        {
            var replyExists = await _context.Messages.AnyAsync(m =>
                m.Id == request.ReplyToMessageId.Value
                && m.ChatId == request.ChatId
                && m.IsDeleted != true);

            if (!replyExists)
                return Result.NotFound("Сообщение для ответа не найдено в этом чате");
        }

        if (request.ForwardedFromMessageId.HasValue)
        {
            var originalExists = await _context.Messages.AnyAsync(m =>
                m.Id == request.ForwardedFromMessageId.Value
                && m.IsDeleted != true);

            if (!originalExists)
                return Result.NotFound("Оригинальное сообщение для пересылки не найдено");
        }

        return Result.Success();
    }

    private static Message BuildMessage(int senderId, CreateMessageRequest request) => new()
    {
        ChatId = request.ChatId,
        SenderId = senderId,
        Content = request.IsVoiceMessage ? null : request.Content,
        IsDeleted = false,
        ReplyToMessageId = request.ReplyToMessageId,
        ForwardedFromMessageId = request.ForwardedFromMessageId,
    };

    private Result AttachVoiceMessage(Message message, CreateMessageRequest request)
    {
        if (string.IsNullOrEmpty(request.VoiceFileUrl))
            return Result.Failure("Голосовое сообщение должно содержать аудиофайл");

        message.VoiceMessage = new VoiceMessage
        {
            DurationSeconds = request.VoiceDurationSeconds ?? 0,
            TranscriptionStatus = TranscriptionStatus.Pending,
            FilePath = StripBaseUrl(request.VoiceFileUrl),
            FileName = request.VoiceFileName ?? "voice.wav",
            ContentType = request.VoiceContentType ?? "audio/wav",
            FileSize = request.VoiceFileSize ?? 0
        };

        return Result.Success();
    }

    private void AttachFiles(Message message, List<MessageFileDto>? files)
    {
        if (files is not { Count: > 0 })
            return;

        foreach (var f in files)
        {
            message.MessageFiles.Add(new MessageFile
            {
                FileName = f.FileName,
                ContentType = f.ContentType,
                Path = StripFileUrl(f.Url)
            });
        }
    }

    private async Task<Result<MessageDto>> PostCreateAsync(Message message, int senderId)
    {
        var createdMessage = await MessagesWithIncludes().FirstAsync(m => m.Id == message.Id);
        var messageDto = createdMessage.ToDto(senderId, urlBuilder);

        await hubNotifier.SendToChatAsync(message.ChatId, "ReceiveMessageDto", messageDto);
        await NotifyAndUpdateUnreadAsync(messageDto);

        if (createdMessage.IsVoiceMessage)
        {
            await transcriptionQueue.EnqueueAsync(message.Id);
            _logger.LogDebug("Голосовое {MessageId} → очередь транскрибации", message.Id);
        }

        _logger.LogInformation("Сообщение {MessageId} создано в чате {ChatId}",
            message.Id, message.ChatId);

        return Result<MessageDto>.Success(messageDto);
    }

    #endregion

    #region Get Messages

    public async Task<Result<PagedMessagesDto>> GetChatMessagesAsync(
        int chatId, int userId, int page, int pageSize)
    {
        var accessResult = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (accessResult.IsFailure)
            return Result<PagedMessagesDto>.FromFailure(accessResult);

        var (normalizedPage, normalizedPageSize) = NormalizePagination(page, pageSize, _settings.MaxPageSize);

        var query = MessagesWithIncludes()
            .Where(m => m.ChatId == chatId && m.IsDeleted != true)
            .OrderByDescending(m => m.CreatedAt)
            .AsNoTracking();

        var totalCount = await query.CountAsync();
        var skipCount = (normalizedPage - 1) * normalizedPageSize;

        var messages = await Paginate(query, normalizedPage, normalizedPageSize).ToListAsync();

        var messageDtos = messages
            .Select(m => m.ToDto(userId, urlBuilder))
            .Reverse()
            .ToList();

        return Result<PagedMessagesDto>.Success(new PagedMessagesDto
        {
            Messages = messageDtos,
            CurrentPage = normalizedPage,
            TotalCount = totalCount,
            HasMoreMessages = totalCount > skipCount + normalizedPageSize,
            HasNewerMessages = false
        });
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesAroundAsync(
        int chatId, int messageId, int userId, int count)
    {
        var accessResult = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (accessResult.IsFailure)
            return Result<PagedMessagesDto>.FromFailure(accessResult);

        var halfCount = count / 2;

        var beforeAndTarget = await MessagesWithIncludes()
            .Where(m => m.ChatId == chatId && m.Id <= messageId && m.IsDeleted != true)
            .OrderByDescending(m => m.Id)
            .Take(halfCount + 1)
            .AsNoTracking()
            .ToListAsync();

        var after = await MessagesWithIncludes()
            .Where(m => m.ChatId == chatId && m.Id > messageId && m.IsDeleted != true)
            .OrderBy(m => m.Id)
            .Take(halfCount)
            .AsNoTracking()
            .ToListAsync();

        var messages = beforeAndTarget
            .OrderBy(m => m.Id)
            .Concat(after)
            .Select(m => m.ToDto(userId, urlBuilder))
            .ToList();

        var oldestLoadedId = beforeAndTarget.Count > 0 ? beforeAndTarget.Min(m => m.Id) : messageId;
        var hasOlder = await _context.Messages.AnyAsync(m =>
            m.ChatId == chatId && m.Id < oldestLoadedId && m.IsDeleted != true);

        var newestLoadedId = after.Count > 0 ? after.Max(m => m.Id) : messageId;
        var hasNewer = await _context.Messages.AnyAsync(m =>
            m.ChatId == chatId && m.Id > newestLoadedId && m.IsDeleted != true);

        return Result<PagedMessagesDto>.Success(new PagedMessagesDto
        {
            Messages = messages,
            HasMoreMessages = hasOlder,
            HasNewerMessages = hasNewer,
            TotalCount = messages.Count,
            CurrentPage = 1
        });
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesBeforeAsync(
        int chatId, int messageId, int userId, int count)
    {
        var accessResult = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (accessResult.IsFailure)
            return Result<PagedMessagesDto>.FromFailure(accessResult);

        var messages = await MessagesWithIncludes()
            .Where(m => m.ChatId == chatId && m.Id < messageId && m.IsDeleted != true)
            .OrderByDescending(m => m.Id)
            .Take(count)
            .AsNoTracking()
            .ToListAsync();

        var oldestId = messages.Count > 0 ? messages.Min(m => m.Id) : messageId;
        var hasMore = await _context.Messages.AnyAsync(m =>
            m.ChatId == chatId && m.Id < oldestId && m.IsDeleted != true);

        return Result<PagedMessagesDto>.Success(new PagedMessagesDto
        {
            Messages = [.. messages.OrderBy(m => m.Id).Select(m => m.ToDto(userId, urlBuilder))],
            HasMoreMessages = hasMore,
            HasNewerMessages = true,
            TotalCount = messages.Count,
            CurrentPage = 1
        });
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesAfterAsync(
        int chatId, int messageId, int userId, int count)
    {
        var accessResult = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (accessResult.IsFailure)
            return Result<PagedMessagesDto>.FromFailure(accessResult);

        var messages = await MessagesWithIncludes()
            .Where(m => m.ChatId == chatId && m.Id > messageId && m.IsDeleted != true)
            .OrderBy(m => m.Id)
            .Take(count)
            .AsNoTracking()
            .ToListAsync();

        var newestId = messages.Count > 0 ? messages.Max(m => m.Id) : messageId;
        var hasNewer = await _context.Messages.AnyAsync(m =>
            m.ChatId == chatId && m.Id > newestId && m.IsDeleted != true);

        return Result<PagedMessagesDto>.Success(new PagedMessagesDto
        {
            Messages = [.. messages.Select(m => m.ToDto(userId, urlBuilder))],
            HasMoreMessages = true,
            HasNewerMessages = hasNewer,
            TotalCount = messages.Count,
            CurrentPage = 1
        });
    }

    #endregion

    #region Update

    public async Task<Result<MessageDto>> UpdateMessageAsync(
        int messageId, int userId, UpdateMessageDto dto)
    {
        var message = await MessagesWithIncludes().FirstOrDefaultAsync(m => m.Id == messageId);

        if (message is null)
            return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var accessResult = await accessControl.CheckIsMemberAsync(userId, message.ChatId);
        if (accessResult.IsFailure)
            return Result<MessageDto>.FromFailure(accessResult);

        if (message.SenderId != userId)
            return Result<MessageDto>.Forbidden("Вы можете изменять только свои сообщения");

        if (message.IsSystemMessage)
            return Result<MessageDto>.Failure("Системные сообщения нельзя редактировать");

        if (message.IsDeleted == true)
            return Result<MessageDto>.Failure("Сообщение уже удалено");

        if (message.Polls.Count != 0)
            return Result<MessageDto>.Failure("Нельзя редактировать сообщение с опросом");

        if (message.IsVoiceMessage)
            return Result<MessageDto>.Failure("Нельзя редактировать голосовое сообщение");

        if (message.ForwardedFromMessageId.HasValue)
            return Result<MessageDto>.Failure("Нельзя редактировать пересланное сообщение");

        if (string.IsNullOrWhiteSpace(dto.Content))
            return Result<MessageDto>.Failure("Содержимое сообщения не может быть пустым");

        message.Content = dto.Content.Trim();
        message.EditedAt = appDateTime.UtcNow;

        var saveResult = await SaveChangesAsync();
        if (saveResult.IsFailure)
            return Result<MessageDto>.FromFailure(saveResult);

        var messageDto = message.ToDto(userId, urlBuilder);
        await hubNotifier.SendToChatAsync(message.ChatId, "MessageUpdated", messageDto);

        _logger.LogInformation("Сообщение {MessageId} отредактировано", messageId);
        return Result<MessageDto>.Success(messageDto);
    }

    #endregion

    #region Delete

    public async Task<Result> DeleteMessageAsync(int messageId, int userId)
    {
        var message = await _context.Messages
            .Include(m => m.VoiceMessage)
            .Include(m => m.MessageFiles)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message is null)
            return Result.NotFound($"Сообщение с ID {messageId} не найдено");

        var accessResult = await accessControl.CheckIsMemberAsync(userId, message.ChatId);
        if (accessResult.IsFailure)
            return Result.FromFailure(accessResult);

        if (message.SenderId != userId)
            return Result.Forbidden("Вы можете удалять только свои сообщения");

        if (message.IsSystemMessage)
            return Result.Failure("Системные сообщения нельзя удалить");

        if (message.IsDeleted == true)
            return Result.Failure("Сообщение уже удалено");

        message.IsDeleted = true;
        message.Content = null;
        message.EditedAt = appDateTime.UtcNow;

        if (message.VoiceMessage != null)
        {
            fileService.DeleteFile(message.VoiceMessage.FilePath);
            _context.VoiceMessages.Remove(message.VoiceMessage);
        }

        var saveResult = await SaveChangesAsync();
        if (saveResult.IsFailure)
            return saveResult;

        await hubNotifier.SendToChatAsync(message.ChatId, "MessageDeleted",
            new { MessageId = messageId, message.ChatId });

        _logger.LogInformation("Сообщение {MessageId} удалено", messageId);
        return Result.Success();
    }

    #endregion

    #region Search

    public async Task<Result<SearchMessagesResponseDto>> SearchMessagesAsync(
        int chatId, int userId, string query, int page, int pageSize)
    {
        var accessResult = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (accessResult.IsFailure)
            return Result<SearchMessagesResponseDto>.FromFailure(accessResult);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<SearchMessagesResponseDto>.Success(
                new SearchMessagesResponseDto
                {
                    Messages = [],
                    TotalCount = 0,
                    CurrentPage = page
                });
        }

        var (normalizedPage, normalizedPageSize) = NormalizePagination(page, 20, _settings.MaxPageSize);
        var escapedQuery = EscapeLikePattern(query);

        var baseQuery = MessagesWithIncludes()
            .Where(m => m.ChatId == chatId)
            .Where(m => m.IsDeleted != true)
            .Where(m => !m.IsSystemMessage)
            .Where(m => m.Content != null && EF.Functions.ILike(m.Content, $"%{escapedQuery}%"))
            .OrderByDescending(m => m.CreatedAt)
            .AsNoTracking();

        var totalCount = await baseQuery.CountAsync();
        var messages = await Paginate(baseQuery, normalizedPage, normalizedPageSize).ToListAsync();

        return Result<SearchMessagesResponseDto>.Success(
            new SearchMessagesResponseDto
            {
                Messages = [.. messages.Select(m => m.ToDto(userId, urlBuilder)).Reverse()],
                TotalCount = totalCount,
                CurrentPage = normalizedPage,
                HasMoreMessages = totalCount
                    > ((normalizedPage - 1) * normalizedPageSize) + normalizedPageSize
            });
    }

    public async Task<Result<GlobalSearchResponseDto>> GlobalSearchAsync(
        int userId, string query, int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<GlobalSearchResponseDto>.Success(new GlobalSearchResponseDto
            {
                Chats = [],
                Messages = [],
                CurrentPage = page
            });
        }

        var (normalizedPage, normalizedPageSize) = NormalizePagination(page, 20, 50);
        var escapedQuery = EscapeLikePattern(query);

        var userChatIds = await _context.ChatMembers
            .Where(cm => cm.UserId == userId)
            .Select(cm => cm.ChatId)
            .ToListAsync();

        if (userChatIds.Count == 0)
        {
            return Result<GlobalSearchResponseDto>.Success(
                new GlobalSearchResponseDto
                {
                    Chats = [],
                    Messages = [],
                    CurrentPage = page
                });
        }

        var foundChats = await SearchChatsAsync(userChatIds, escapedQuery, userId);

        var (messages, totalCount, hasMore) = await SearchMessagesGlobalAsync(
            userChatIds, escapedQuery, userId, normalizedPage, normalizedPageSize);

        return Result<GlobalSearchResponseDto>.Success(
            new GlobalSearchResponseDto
            {
                Chats = foundChats,
                Messages = messages,
                TotalChatsCount = foundChats.Count,
                TotalMessagesCount = totalCount,
                CurrentPage = normalizedPage,
                HasMoreMessages = hasMore
            });
    }

    private async Task<List<ChatDto>> SearchChatsAsync(
        List<int> userChatIds, string query, int userId)
    {
        const int maxResults = 5;
        var result = new List<ChatDto>();

        var dialogs = await _context.Chats
            .Where(c => userChatIds.Contains(c.Id))
            .Where(c => c.Type == ChatType.Contact)
            .Include(c => c.ChatMembers).ThenInclude(cm => cm.User)
            .AsNoTracking()
            .ToListAsync();

        foreach (var chat in dialogs)
        {
            var partner = chat.ChatMembers.FirstOrDefault(cm => cm.UserId != userId)?.User;
            if (partner is null) continue;

            var displayName = partner.FormatDisplayName();
            var username = partner.Username ?? "";

            if (displayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || username.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ChatDto
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
            .Where(c => EF.Functions.ILike(c.Name ?? string.Empty, $"%{query}%"))
            .Take(maxResults)
            .AsNoTracking()
            .ToListAsync();

        result.AddRange(groupChats.Select(c => c.ToDto(urlBuilder)));

        return [.. result.Take(maxResults)];
    }

    private async Task<(List<GlobalSearchMessageDto> Messages, int TotalCount, bool HasMore)>
        SearchMessagesGlobalAsync(
            List<int> userChatIds, string query, int userId, int page, int pageSize)
    {
        var messagesQuery = _context.Messages
            .Where(m => userChatIds.Contains(m.ChatId))
            .Where(m => m.IsDeleted != true)
            .Where(m => !m.IsSystemMessage)
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

        var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId);

        var result = messages.ConvertAll(m =>
            CreateGlobalSearchMessageDto(m, query, dialogPartners));

        return (result, totalCount, totalCount > ((page - 1) * pageSize) + pageSize);
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

        return partners.Where(p => p.User != null).ToDictionary(
            p => p.ChatId,
            p => (Name: p.User!.FormatDisplayName(), Avatar: urlBuilder.BuildUrl(p.User.Avatar)));
    }

    private GlobalSearchMessageDto CreateGlobalSearchMessageDto(
        Message message,
        string searchTerm,
        Dictionary<int, (string Name, string? Avatar)> dialogPartners)
    {
        var dto = new GlobalSearchMessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            ChatType = message.Chat.Type,
            SenderId = message.SenderId,
            SenderName = message.Sender?.FormatDisplayName(),
            Content = message.Content,
            CreatedAt = message.CreatedAt,
            HighlightedContent = CreateHighlightedContent(message.Content, searchTerm),
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

    private string StripFileUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        var baseUrl = urlBuilder.BuildUrl("/");
        if (baseUrl != null && url.StartsWith(baseUrl))
        {
            var relative = url[baseUrl.Length..];
            return relative.StartsWith('/') ? relative : "/" + relative;
        }

        return url.StartsWith('/') ? url : "/" + url;
    }

    private string StripBaseUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        var baseUrl = urlBuilder.BuildUrl("/");
        if (baseUrl != null && url.StartsWith(baseUrl))
        {
            var relative = url[baseUrl.Length..];
            return relative.StartsWith('/') ? relative : "/" + relative;
        }

        return url.StartsWith('/') ? url : "/" + url;
    }

    private async Task MarkChatUpdatedAsync(int chatId)
    {
        var chat = await _context.Chats.FindAsync(chatId);
        chat?.LastMessageTime = appDateTime.UtcNow;
    }

    private async Task NotifyAndUpdateUnreadAsync(MessageDto message)
    {
        try
        {
            var usersToNotify = await _context.ChatMembers
                .Where(cm => cm.ChatId == message.ChatId && cm.UserId != message.SenderId)
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
                var unreadResult = await readReceiptService.GetUnreadCountAsync(
                    member.UserId, message.ChatId);
                var unreadCount = unreadResult.IsSuccess ? unreadResult.Value : 0;

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