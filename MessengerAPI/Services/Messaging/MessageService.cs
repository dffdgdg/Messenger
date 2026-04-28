using MessengerAPI.Services.Base;
using MessengerAPI.Services.Chat;
using MessengerAPI.Services.ReadReceipt;
using MessengerShared.DTO.Message;
using System.Text.RegularExpressions;

namespace MessengerAPI.Services.Messaging;

public interface IMessageService
{
    Task<Result<MessageDto>> CreateMessageAsync(int senderId, CreateMessageRequest request);
    Task<Result<MessageDto>> UpdateMessageAsync(int messageId, int userId, UpdateMessageDto dto);
    Task<Result> DeleteMessageAsync(int messageId, int userId);
    Task<Result<MessageDto>> PinMessageAsync(int messageId, int userId);
    Task<Result<MessageDto>> UnpinMessageAsync(int messageId, int userId);
    Task<Result<List<MessageDto>>> GetPinnedMessagesAsync(int chatId, int userId);
    Task<Result<PagedMessagesDto>> GetChatMessagesAsync(int chatId, int userId, int page, int pageSize);
    Task<Result<PagedMessagesDto>> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDto>> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count);
    Task<Result<PagedMessagesDto>> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count);
    Task<Result<SearchMessagesResponseDto>> SearchMessagesAsync(int chatId, int userId, SearchMessagesQueryDto query);
    Task<Result<GlobalSearchResponseDto>> GlobalSearchAsync(int userId, GlobalSearchQueryDto query);
}

public partial class MessageService(MessengerDbContext context, IAccessControlService accessControl, IHubNotifier hubNotifier,
    INotificationService notificationService, IReadReceiptService readReceiptService, IUrlBuilder urlBuilder, IFileService fileService,
    IOptions<MessengerSettings> settings, AppDateTime appDateTime, ILogger<MessageService> logger)
    : BaseService<MessageService>(context, logger), IMessageService
{
    private readonly MessengerSettings _settings = settings.Value;

    [GeneratedRegex(@"(?<![a-z0-9_])@([a-z0-9_]{3,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    #region Base Query & Helpers

    private IQueryable<Message> MessagesWithIncludes() => _context.Messages.Include(m => m.Sender).Include(m => m.TargetUser)
        .Include(m => m.VoiceMessage).Include(m => m.MessageFiles).Include(m => m.Polls).ThenInclude(p => p.PollOptions)
        .ThenInclude(o => o.PollVotes).Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.VoiceMessage)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.MessageFiles).Include(m => m.ForwardedFromMessage)
        .ThenInclude(f => f!.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes);

    private IQueryable<Message> MessagesLight() => _context.Messages
        .Include(m => m.Sender).Include(m => m.VoiceMessage).Include(m => m.MessageFiles)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.VoiceMessage)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.MessageFiles)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Polls).ThenInclude(p => p.PollOptions).ThenInclude(o => o.PollVotes)
        .AsNoTracking();

    private async Task<Result?> EnsureAccessAsync(int userId, int chatId)
        => await accessControl.EnsureMemberOfAsync(userId, chatId);

    private string StripBaseUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        var baseUrl = urlBuilder.BuildUrl("/");
        if (baseUrl != null && url.StartsWith(baseUrl))
        {
            var rel = url[baseUrl.Length..];
            return rel.StartsWith('/') ? rel : "/" + rel;
        }
        return url.StartsWith('/') ? url : "/" + url;
    }

    private static PagedMessagesDto BuildPagedResult(List<MessageDto> messages, bool hasOlder, bool hasNewer) => new()
    {
        Messages = messages,
        HasMoreMessages = hasOlder,
        HasNewerMessages = hasNewer,
        TotalCount = messages.Count,
        CurrentPage = 1
    };

    private static string EscapeLikePattern(string pattern)
        => string.IsNullOrEmpty(pattern) ? pattern : pattern.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    private static IQueryable<Message> ApplyMessageSearchFilters(IQueryable<Message> query, int? senderId, DateTime? dateFrom, DateTime? dateTo,
        bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText)
    {
        if (senderId.HasValue)
            query = query.Where(m => m.SenderId == senderId.Value);

        if (dateFrom.HasValue)
            query = query.Where(m => m.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(m => m.CreatedAt < dateTo.Value.AddDays(1));

        if (hasFiles)
            query = query.Where(m => m.MessageFiles.Any());

        if (hasVoice)
            query = query.Where(m => m.VoiceMessage != null);

        if (hasPoll)
            query = query.Where(m => m.Polls.Any());

        if (onlyText)
            query = query.Where(m => !m.MessageFiles.Any() && m.VoiceMessage == null && !m.Polls.Any());

        return query;
    }

    private static IQueryable<Message> ApplyMessageSorting(IQueryable<Message> query, bool oldestFirst)
        => oldestFirst ? query.OrderBy(m => m.CreatedAt) : query.OrderByDescending(m => m.CreatedAt);

    #endregion

    #region Create

    public async Task<Result<MessageDto>> CreateMessageAsync(int senderId, CreateMessageRequest request)
    {
        var access = await EnsureAccessAsync(senderId, request.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (!request.IsVoiceMessage && !request.ForwardedFromMessageId.HasValue
            && string.IsNullOrWhiteSpace(request.Content) && request.Files is not { Count: > 0 })
        {
            return Result<MessageDto>.Failure("Сообщение должно содержать текст или файлы");
        }

        var refCheck = await ValidateReferencesAsync(request);
        if (refCheck.IsFailure) return refCheck.As<MessageDto>();

        var message = new Message
        {
            ChatId = request.ChatId,
            SenderId = senderId,
            Content = request.IsVoiceMessage ? null : request.Content,
            IsDeleted = false,
            ReplyToMessageId = request.ReplyToMessageId,
            ForwardedFromMessageId = request.ForwardedFromMessageId
        };
        _context.Messages.Add(message);

        if (request.IsVoiceMessage)
        {
            if (string.IsNullOrEmpty(request.VoiceFileUrl))
                return Result<MessageDto>.Failure("Голосовое сообщение должно содержать аудиофайл");

            message.VoiceMessage = new VoiceMessage
            {
                DurationSeconds = request.VoiceDurationSeconds ?? 0,
                Waveform = request.VoiceWaveform,
                FilePath = StripBaseUrl(request.VoiceFileUrl),
                FileName = request.VoiceFileName ?? "voice.wav",
                ContentType = request.VoiceContentType ?? "audio/wav",
                FileSize = request.VoiceFileSize ?? 0
            };
        }

        if (request.Files is { Count: > 0 })
        {
            foreach (var f in request.Files)
            {
                message.MessageFiles.Add(new MessageFile
                {
                    FileName = f.FileName,
                    ContentType = f.ContentType,
                    Path = StripBaseUrl(f.Url)
                });
            }
        }

        await MarkChatUpdatedAsync(request.ChatId);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<MessageDto>();

        var created = await MessagesWithIncludes().AsNoTracking().FirstAsync(m => m.Id == message.Id);
        var senderDto = created.ToDto(senderId, urlBuilder);

        await BroadcastToMembersAsync(created, message.ChatId, "ReceiveMessageDto");
        await NotifyAndUpdateUnreadAsync(senderDto);
        LogMessageCreated(message.Id, message.ChatId);

        return Result<MessageDto>.Success(senderDto);
    }

    private async Task<Result> ValidateReferencesAsync(CreateMessageRequest request)
    {
        if (request.ReplyToMessageId.HasValue && !await _context.Messages.AnyAsync(m => m.Id == request.ReplyToMessageId.Value && m.ChatId == request.ChatId && m.IsDeleted != true))
        {
            return Result.NotFound("Сообщение для ответа не найдено в этом чате");
        }

        if (request.ForwardedFromMessageId.HasValue && !await _context.Messages.AnyAsync(m => m.Id == request.ForwardedFromMessageId.Value && m.IsDeleted != true))
        {
            return Result.NotFound("Оригинальное сообщение для пересылки не найдено");
        }

        return Result.Success();
    }

    #endregion

    #region Get Messages

    public async Task<Result<PagedMessagesDto>> GetChatMessagesAsync(int chatId, int userId, int page, int pageSize)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var (np, nps) = NormalizePagination(page, pageSize, _settings.MaxPageSize);

        var query = MessagesWithIncludes().Where(m => m.ChatId == chatId && m.IsDeleted != true).OrderByDescending(m => m.CreatedAt).AsNoTracking();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        if (cutoff.HasValue)
            query = (IOrderedQueryable<Message>)query.Where(m => m.CreatedAt >= cutoff.Value);

        var total = await query.CountAsync();
        var skip = (np - 1) * nps;
        var messages = await Paginate(query, np, nps).ToListAsync();

        return Result<PagedMessagesDto>.Success(new PagedMessagesDto
        {
            Messages = [.. messages.Select(m => m.ToDto(userId, urlBuilder)).Reverse()],
            CurrentPage = np,
            TotalCount = total,
            HasMoreMessages = total > skip + nps,
            HasNewerMessages = false
        });
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var half = count / 2;
        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var beforeQuery = MessagesWithIncludes()
            .Where(m => m.ChatId == chatId && m.Id <= messageId && m.IsDeleted != true)
            .AsNoTracking();
        if (cutoff.HasValue)
            beforeQuery = beforeQuery.Where(m => m.CreatedAt >= cutoff.Value);

        var afterQuery = MessagesWithIncludes()
            .Where(m => m.ChatId == chatId && m.Id > messageId && m.IsDeleted != true)
            .AsNoTracking();
        if (cutoff.HasValue)
            afterQuery = afterQuery.Where(m => m.CreatedAt >= cutoff.Value);

        var before = await beforeQuery.OrderByDescending(m => m.Id).Take(half + 1).ToListAsync();
        var after = await afterQuery.OrderBy(m => m.Id).Take(half).ToListAsync();

        var msgs = before.OrderBy(m => m.Id).Concat(after).Select(m => m.ToDto(userId, urlBuilder)).ToList();

        var oldestId = before.Count > 0 ? before.Min(m => m.Id) : messageId;
        var newestId = after.Count > 0 ? after.Max(m => m.Id) : messageId;

        var hasOlderQuery = _context.Messages.Where(m => m.ChatId == chatId && m.Id < oldestId && m.IsDeleted != true);
        if (cutoff.HasValue)
            hasOlderQuery = hasOlderQuery.Where(m => m.CreatedAt >= cutoff.Value);

        var hasNewerQuery = _context.Messages.Where(m => m.ChatId == chatId && m.Id > newestId && m.IsDeleted != true);
        if (cutoff.HasValue)
            hasNewerQuery = hasNewerQuery.Where(m => m.CreatedAt >= cutoff.Value);

        return Result<PagedMessagesDto>.Success(BuildPagedResult(msgs,
            await hasOlderQuery.AnyAsync(),
            await hasNewerQuery.AnyAsync()));
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        var query = MessagesLight().Where(m => m.ChatId == chatId && m.Id < messageId && m.IsDeleted != true);

        if (cutoff.HasValue)
            query = query.Where(m => m.CreatedAt >= cutoff.Value);

        var messages = await query.OrderByDescending(m => m.Id).Take(count).ToListAsync();
        var oldestId = messages.Count > 0 ? messages.Min(m => m.Id) : messageId;

        var hasOlderQuery = _context.Messages.Where(m => m.ChatId == chatId && m.Id < oldestId && m.IsDeleted != true);
        if (cutoff.HasValue)
            hasOlderQuery = hasOlderQuery.Where(m => m.CreatedAt >= cutoff.Value);

        return Result<PagedMessagesDto>.Success(BuildPagedResult([.. messages.OrderBy(m => m.Id).Select(m => m.ToDto(userId, urlBuilder))],
            await hasOlderQuery.AnyAsync(), hasNewer: true));
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        var query = MessagesLight().Where(m => m.ChatId == chatId && m.Id > messageId && m.IsDeleted != true);

        if (cutoff.HasValue)
            query = query.Where(m => m.CreatedAt >= cutoff.Value);

        var messages = await query.OrderBy(m => m.Id).Take(count).ToListAsync();
        var newestId = messages.Count > 0 ? messages.Max(m => m.Id) : messageId;

        var hasNewerQuery = _context.Messages.Where(m => m.ChatId == chatId && m.Id > newestId && m.IsDeleted != true);
        if (cutoff.HasValue)
            hasNewerQuery = hasNewerQuery.Where(m => m.CreatedAt >= cutoff.Value);

        return Result<PagedMessagesDto>.Success(BuildPagedResult([.. messages.Select(m => m.ToDto(userId, urlBuilder))], hasOlder: true,
            await hasNewerQuery.AnyAsync()));
    }

    #endregion

    #region Update

    public async Task<Result<MessageDto>> UpdateMessageAsync(int messageId, int userId, UpdateMessageDto dto)
    {
        var message = await MessagesWithIncludes().FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null) return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (message.SenderId != userId)
            return Result<MessageDto>.Forbidden("Вы можете изменять только свои сообщения");

        var error = message switch
        {
            { IsSystemMessage: true } => "Системные сообщения нельзя редактировать",
            { IsDeleted: true } => "Сообщение уже удалено",
            _ when message.Polls.Count != 0 => "Нельзя редактировать сообщение с опросом",
            { IsVoiceMessage: true } => "Нельзя редактировать голосовое сообщение",
            { ForwardedFromMessageId: not null } => "Нельзя редактировать пересланное сообщение",
            _ when string.IsNullOrWhiteSpace(dto.Content) => "Содержимое сообщения не может быть пустым",
            _ => null
        };
        if (error != null) return Result<MessageDto>.Failure(error);

        message.Content = dto.Content!.Trim();
        message.EditedAt = appDateTime.UtcNow;
        message.IsPinned = false;
        message.PinnedAt = null;
        message.PinnedByUserId = null;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<MessageDto>();

        await BroadcastToMembersAsync(message, message.ChatId, "MessageUpdated");
        LogMessageUpdated(messageId);

        return Result<MessageDto>.Success(message.ToDto(userId, urlBuilder));
    }

    #endregion

    #region Delete

    public async Task<Result> DeleteMessageAsync(int messageId, int userId)
    {
        var message = await _context.Messages.Include(m => m.VoiceMessage).Include(m => m.MessageFiles).FirstOrDefaultAsync(m => m.Id == messageId);

        if (message is null) return Result.NotFound($"Сообщение с ID {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access.IsFailure) return access;

        if (message.SenderId != userId)
            return Result.Forbidden("Вы можете удалять только свои сообщения");

        var error = message switch
        {
            { IsSystemMessage: true } => "Системные сообщения нельзя удалить",
            { IsDeleted: true } => "Сообщение уже удалено",
            _ => null
        };
        if (error != null) return Result.Failure(error);

        message.IsDeleted = true;
        message.Content = null;
        message.EditedAt = appDateTime.UtcNow;

        if (message.VoiceMessage != null)
        {
            fileService.DeleteFile(message.VoiceMessage.FilePath);
            _context.VoiceMessages.Remove(message.VoiceMessage);
        }

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save;

        await hubNotifier.SendToChatAsync(message.ChatId, "MessageDeleted",
            new { MessageId = messageId, message.ChatId });
        LogMessageDeleted(messageId);

        return Result.Success();
    }

    #endregion

    #region Pinned Messages

    public async Task<Result<MessageDto>> PinMessageAsync(int messageId, int userId)
    {
        var message = await MessagesWithIncludes().FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null) return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (message.IsDeleted == true)
            return Result<MessageDto>.Failure("Нельзя закрепить удаленное сообщение");

        message.IsPinned = true;
        message.PinnedAt = appDateTime.UtcNow;
        message.PinnedByUserId = userId;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<MessageDto>();

        await BroadcastToMembersAsync(message, message.ChatId, "MessageUpdated");
        return Result<MessageDto>.Success(message.ToDto(userId, urlBuilder));
    }

    public async Task<Result<MessageDto>> UnpinMessageAsync(int messageId, int userId)
    {
        var message = await MessagesWithIncludes().FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null) return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (!message.IsPinned)
            return Result<MessageDto>.Failure("Сообщение уже не закреплено");

        message.IsPinned = false;
        message.PinnedAt = null;
        message.PinnedByUserId = null;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<MessageDto>();

        await BroadcastToMembersAsync(message, message.ChatId, "MessageUpdated");
        return Result<MessageDto>.Success(message.ToDto(userId, urlBuilder));
    }

    public async Task<Result<List<MessageDto>>> GetPinnedMessagesAsync(int chatId, int userId)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<List<MessageDto>>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        var query = MessagesLight().Where(m => m.ChatId == chatId && m.IsPinned && m.IsDeleted != true).AsNoTracking();

        if (cutoff.HasValue)
            query = query.Where(m => m.CreatedAt >= cutoff.Value);

        var pinned = await query.OrderByDescending(m => m.PinnedAt ?? m.CreatedAt).ToListAsync();
        return Result<List<MessageDto>>.Success([.. pinned.Select(m => m.ToDto(userId, urlBuilder))]);
    }

    #endregion

    #region Search

    public async Task<Result<SearchMessagesResponseDto>> SearchMessagesAsync(int chatId, int userId, SearchMessagesQueryDto query)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<SearchMessagesResponseDto>();

        var (np, nps) = NormalizePagination(query.Page, query.PageSize, _settings.MaxPageSize);
        var hasQuery = !string.IsNullOrWhiteSpace(query.Query);
        var escaped = hasQuery ? EscapeLikePattern(query.Query) : string.Empty;

        var q = MessagesWithIncludes().Where(m => m.ChatId == chatId && m.IsDeleted != true && !m.IsSystemMessage
            && (!hasQuery || (m.Content != null && EF.Functions.ILike(m.Content, $"%{escaped}%")))).AsNoTracking();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        if (cutoff.HasValue)
            q = q.Where(m => m.CreatedAt >= cutoff.Value);

        q = ApplyMessageSearchFilters(q, query.SenderId, query.DateFrom, query.DateTo,
            query.HasFiles == true, query.HasVoice == true, query.HasPoll == true, query.OnlyText == true);

        q = ApplyMessageSorting(q, query.OldestFirst);

        var total = await q.CountAsync();
        var messages = await Paginate(q, np, nps).ToListAsync();
        var ordered = query.OldestFirst ? messages.AsEnumerable() : messages.AsEnumerable().Reverse();

        return Result<SearchMessagesResponseDto>.Success(new()
        {
            Messages = [.. ordered.Select(m => m.ToDto(userId, urlBuilder))],
            TotalCount = total,
            CurrentPage = np,
            HasMoreMessages = total > ((np - 1) * nps) + nps
        });
    }

    private async Task<(List<GlobalSearchMessageDto>, int Total, bool HasMore)> SearchMessagesGlobalAsync(List<int> chatIds, string escapedQuery, int userId, GlobalSearchQueryDto query,
        int page, int pageSize, bool hasQuery)
    {
        var q = _context.Messages.Where(m => chatIds.Contains(m.ChatId) && m.IsDeleted != true && !m.IsSystemMessage
            && (!hasQuery || (m.Content != null && EF.Functions.ILike(m.Content, $"%{escapedQuery}%")))).Include(m => m.Sender).Include(m => m.Chat)
            .Include(m => m.MessageFiles).Include(m => m.VoiceMessage).Include(m => m.Polls).AsNoTracking();

        q = ApplyMessageSearchFilters(q, query.SenderId, query.DateFrom, query.DateTo, query.HasFiles == true, query.HasVoice == true,
            query.HasPoll == true, query.OnlyText == true);

        q = ApplyMessageSorting(q, query.OldestFirst);

        q = await ApplyGlobalHistoryFilter(q, chatIds, userId);

        var total = await q.CountAsync();
        var messages = await Paginate(q, page, pageSize).ToListAsync();

        var dialogIds = messages.Where(m => m.Chat.Type == ChatType.Contact).Select(m => m.ChatId).Distinct().ToList();
        var partners = await GetDialogPartnersAsync(dialogIds, userId);

        return (messages.ConvertAll(m => BuildSearchDto(m, escapedQuery, partners)), total, total > ((page - 1) * pageSize) + pageSize);
    }

    public async Task<Result<GlobalSearchResponseDto>> GlobalSearchAsync(int userId, GlobalSearchQueryDto query)
    {
        var (np, nps) = NormalizePagination(query.Page, query.PageSize, 50);

        var hasQuery = !string.IsNullOrWhiteSpace(query.Query);
        var escaped = hasQuery ? EscapeLikePattern(query.Query) : string.Empty;

        var chatIds = await _context.ChatMembers.Where(cm => cm.UserId == userId).Select(cm => cm.ChatId).ToListAsync();

        if (chatIds.Count == 0)
            return Result<GlobalSearchResponseDto>.Success(new() { Chats = [], Messages = [], CurrentPage = query.Page });

        if (query.FilterChatId.HasValue)
        {
            chatIds = [.. chatIds.Where(id => id == query.FilterChatId.Value)];
        }

        var chats = (hasQuery && query.FilterChatId == null) ? await SearchChatsAsync(chatIds, escaped, userId, hasQuery) : [];

        var (msgs, total, hasMore) = await SearchMessagesGlobalAsync(chatIds, escaped, userId, query, np, nps, hasQuery);

        return Result<GlobalSearchResponseDto>.Success(new()
        {
            Chats = chats,
            Messages = msgs,
            TotalChatsCount = chats.Count,
            TotalMessagesCount = total,
            CurrentPage = np,
            HasMoreMessages = hasMore
        });
    }

    private async Task<List<ChatDto>> SearchChatsAsync(List<int> chatIds, string query, int userId, bool hasQuery)
    {
        const int max = 5;
        var result = new List<ChatDto>();

        var dialogs = await _context.Chats.Where(c => chatIds.Contains(c.Id) && c.Type == ChatType.Contact).Include(c => c.ChatMembers).ThenInclude(cm => cm.User).AsNoTracking().ToListAsync();

        foreach (var chat in dialogs)
        {
            var partner = chat.ChatMembers.FirstOrDefault(cm => cm.UserId != userId)?.User;
            if (partner is null) continue;

            var name = partner.FormatDisplayName();
            if (!hasQuery || name.Contains(query, StringComparison.OrdinalIgnoreCase) || (partner.Username ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ChatDto
                {
                    Id = chat.Id,
                    Name = name,
                    Type = chat.Type,
                    Avatar = urlBuilder.BuildUrl(partner.Avatar),
                    LastMessageDate = chat.LastMessageTime
                });
            }
        }

        var groups = await _context.Chats.Where(c => chatIds.Contains(c.Id) && c.Type != ChatType.Contact && (!hasQuery || EF.Functions.ILike(c.Name ?? "", $"%{query}%"))).Take(max).AsNoTracking().ToListAsync();

        result.AddRange(groups.Select(c => c.ToDto(urlBuilder)));
        return [.. result.Take(max)];
    }

    private async Task<Dictionary<int, (string Name, string? Avatar)>> GetDialogPartnersAsync(List<int> chatIds, int userId)
    {
        if (chatIds.Count == 0) return [];

        return (await _context.ChatMembers.Where(cm => chatIds.Contains(cm.ChatId) && cm.UserId != userId).Include(cm => cm.User).AsNoTracking().ToListAsync())
            .Where(p => p.User != null).ToDictionary(p => p.ChatId, p => (p.User!.FormatDisplayName(), urlBuilder.BuildUrl(p.User.Avatar)));
    }

    private GlobalSearchMessageDto BuildSearchDto(Message m, string term, Dictionary<int, (string Name, string? Avatar)> partners)
    {
        var dto = new GlobalSearchMessageDto
        {
            Id = m.Id,
            ChatId = m.ChatId,
            ChatType = m.Chat.Type,
            SenderId = m.SenderId,
            SenderName = m.Sender?.FormatDisplayName(),
            Content = m.Content,
            CreatedAt = m.CreatedAt,
            HighlightedContent = Highlight(m.Content, term),
            HasFiles = m.MessageFiles?.Count > 0,
            HasVoice = m.VoiceMessage != null,
            HasPoll = m.Polls?.Count > 0
        };

        if (m.Chat.Type == ChatType.Contact && partners.TryGetValue(m.ChatId, out var p))
            (dto.ChatName, dto.ChatAvatar) = p;
        else
            (dto.ChatName, dto.ChatAvatar) = (m.Chat.Name, urlBuilder.BuildUrl(m.Chat.Avatar));

        return dto;
    }

    private static string? Highlight(string? content, string term)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(term)) return content;
        var idx = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content.Length > 100 ? content[..100] + "..." : content;

        const int ctx = 40;
        var start = Math.Max(0, idx - ctx);
        var end = Math.Min(content.Length, idx + term.Length + ctx);
        return (start > 0 ? "..." : "") + content[start..end] + (end < content.Length ? "..." : "");
    }

    #endregion

    #region Private Methods

    private async Task BroadcastToMembersAsync(Message message, int chatId, string hubMethod)
    {
        var memberIds = await _context.ChatMembers.Where(cm => cm.ChatId == chatId).Select(cm => cm.UserId).ToListAsync();

        foreach (var memberId in memberIds)
        {
            var dto = message.ToDto(memberId, urlBuilder);
            await hubNotifier.SendToUserAsync(memberId, hubMethod, dto);
        }
    }

    private async Task MarkChatUpdatedAsync(int chatId)
        => (await _context.Chats.FindAsync(chatId))?.LastMessageTime = appDateTime.UtcNow;

    private async Task NotifyAndUpdateUnreadAsync(MessageDto message)
    {
        try
        {
            var mentionedUsernames = ExtractMentionedUsernames(message.Content);
            var members = await _context.ChatMembers.Where(cm => cm.ChatId == message.ChatId && cm.UserId != message.SenderId).Select(cm => new
            {
                cm.UserId,
                cm.User.Username,
                cm.NotificationsEnabled,
                GlobalEnabled = cm.User.UserSetting == null || cm.User.UserSetting.NotificationsEnabled
            }).ToListAsync();

            foreach (var m in members)
            {
                var unread = await readReceiptService.GetUnreadCountAsync(m.UserId, message.ChatId);
                await hubNotifier.SendToUserAsync(m.UserId, "UnreadCountUpdated", message.ChatId, unread.IsSuccess ? unread.Value : 0);

                if (!m.GlobalEnabled) continue;

                var isMentioned = !string.IsNullOrWhiteSpace(m.Username) && mentionedUsernames.Contains(m.Username!);

                if (isMentioned)
                {
                    await notificationService.SendMentionNotificationAsync(m.UserId, message);
                    continue;
                }

                if (m.NotificationsEnabled)
                    await notificationService.SendNotificationAsync(m.UserId, message);
            }
        }
        catch (Exception ex) { LogNotificationError(message.ChatId, ex); }
    }

    private static HashSet<string> ExtractMentionedUsernames(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MentionRegex().Matches(content))
        {
            if (match.Groups.Count < 2) continue;
            var username = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(username))
                result.Add(username);
        }
        return result;
    }

    /// <summary>
    /// Возвращает минимальную дату, с которой пользователь видит сообщения в чате.
    /// null — история не скрыта (показывать все).
    /// </summary>
    private async Task<DateTime?> GetHistoryCutoffAsync(int chatId, int userId)
    {
        var chat = await _context.Chats.AsNoTracking().Select(c => new { c.ShowHistoryForNewMembers }).FirstOrDefaultAsync(c => c.ShowHistoryForNewMembers);

        if (chat?.ShowHistoryForNewMembers != false)
            return null;

        var role = await accessControl.GetRoleAsync(userId, chatId);
        if (role is ChatRole.Owner or ChatRole.Admin)
            return null;

        var member = await accessControl.GetChatMemberAsync(userId, chatId);
        return member?.JoinedAt ?? DateTime.MinValue;
    }

    private async Task<IQueryable<Message>> ApplyGlobalHistoryFilter(IQueryable<Message> query, List<int> chatIds, int userId)
    {
        var chats = await _context.Chats
            .Where(c => chatIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ShowHistoryForNewMembers })
            .ToListAsync();

        var hiddenChatIds = new List<int>();
        var joinedDates = new Dictionary<int, DateTime>();

        foreach (var c in chats)
        {
            if (c.ShowHistoryForNewMembers) continue;

            var role = await accessControl.GetRoleAsync(userId, c.Id);
            if (role is ChatRole.Owner or ChatRole.Admin) continue;

            var member = await accessControl.GetChatMemberAsync(userId, c.Id);
            if (member != null)
            {
                hiddenChatIds.Add(c.Id);
                joinedDates[c.Id] = member.JoinedAt;
            }
        }

        if (hiddenChatIds.Count == 0) return query;

        return query.Where(m => !hiddenChatIds.Contains(m.ChatId) || m.CreatedAt >= joinedDates[m.ChatId]);
    }
    #endregion

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "Сообщение {MessageId} создано в чате {ChatId}")]
    private partial void LogMessageCreated(int messageId, int chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Сообщение {MessageId} отредактировано")]
    private partial void LogMessageUpdated(int messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Сообщение {MessageId} удалено")]
    private partial void LogMessageDeleted(int messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ошибка уведомлений для чата {ChatId}")]
    private partial void LogNotificationError(int chatId, Exception ex);

    #endregion
}