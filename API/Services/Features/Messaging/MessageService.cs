using API.Services.Base;
using API.Services.Infrastructure.Bundles;
using API.Services.Infrastructure.Security;
using Shared.DTO.Message;
using System.Text.RegularExpressions;

namespace API.Services.Messaging;

public partial class MessageService(MessengerDbContext context, ChatBundle chat, MediaBundle media, UrlBundle url,
    IReadReceiptService readReceiptService, IOptions<MessengerSettings> settings, ILogger<MessageService> logger)
    : BaseService<MessageService>(context, logger), IMessageService
{
    private readonly IAccessControlService accessControl = chat.Cache.AccessControl;
    private readonly IHubNotifier hubNotifier = chat.Notifications.HubNotifier;
    private readonly INotificationService notificationService = chat.Notifications.NotificationService;
    private readonly IUrlBuilder urlBuilder = url.UrlBuilder;
    private readonly IFileService fileService = media.FileService;
    private readonly MessengerSettings _settings = settings.Value;
    private readonly AppDateTime appDateTime = chat.Time.AppDateTime;

    [GeneratedRegex(@"(?<![a-z0-9_])@([a-z0-9_]{3,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    #region Base Query & Helpers

    private IQueryable<UserMessage> UserMessagesWithIncludes() => _context.UserMessages.Include(m => m.Sender).Include(m => m.VoiceMessage)
        .Include(m => m.MessageFiles).Include(m => m.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes)
        .Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.VoiceMessage).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.MessageFiles)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes);

    private IQueryable<UserMessage> UserMessagesLight() => _context.UserMessages.Include(m => m.Sender).Include(m => m.VoiceMessage)
        .Include(m => m.MessageFiles).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender).Include(m => m.ForwardedFromMessage)
        .ThenInclude(f => f!.VoiceMessage).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.MessageFiles)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes).AsNoTracking();

    private async Task<List<Message>> LoadAllMessagesAsync(int chatId, int? beforeId = null, int? afterId = null, DateTime? cutoff = null, int? take = null, bool oldestFirst = false)
    {
        var userQ = _context.UserMessages.Include(m => m.Sender).Include(m => m.VoiceMessage).Include(m => m.MessageFiles)
            .Include(m => m.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes).Include(m => m.ReplyToMessage)
            .Where(m => m.ChatId == chatId && m.IsDeleted != true).AsNoTracking();

        var sysQ = _context.SystemMessages.Include(m => m.Initiator).Include(m => m.TargetUser).Where(m => m.ChatId == chatId && m.IsDeleted != true).AsNoTracking();

        if (beforeId.HasValue)
        {
            userQ = userQ.Where(m => m.Id <= beforeId.Value);
            sysQ = sysQ.Where(m => m.Id <= beforeId.Value);
        }

        if (afterId.HasValue)
        {
            userQ = userQ.Where(m => m.Id > afterId.Value);
            sysQ = sysQ.Where(m => m.Id > afterId.Value);
        }

        if (cutoff.HasValue)
        {
            userQ = userQ.Where(m => m.CreatedAt >= cutoff.Value);
            sysQ = sysQ.Where(m => m.CreatedAt >= cutoff.Value);
        }

        var userMessages = await userQ.ToListAsync();
        var sysMessages = await sysQ.ToListAsync();

        var all = userMessages.Cast<Message>().Concat(sysMessages);

        all = oldestFirst ? all.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id) : all.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id);

        if (take.HasValue)
            all = all.Take(take.Value);

        return [.. all];
    }

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

    private static IQueryable<UserMessage> ApplyMessageSearchFilters(IQueryable<UserMessage> query, int? senderId, DateTime? dateFrom, DateTime? dateTo,
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
            query = query.Where(m => m.Poll != null);
        if (onlyText)
            query = query.Where(m => !m.MessageFiles.Any() && m.VoiceMessage == null && m.Poll == null);

        return query;
    }

    private static IQueryable<T> ApplyMessageSorting<T>(IQueryable<T> query, bool oldestFirst) where T : Message
        => oldestFirst ? query.OrderBy(m => m.CreatedAt) : query.OrderByDescending(m => m.CreatedAt);

    #endregion

    #region Create

    public async Task<Result<MessageDto>> CreateMessageAsync(int senderId, CreateMessageRequest request)
    {
        var access = await EnsureAccessAsync(senderId, request.ChatId);
        if (access!.IsFailure) return access.As<MessageDto>();

        if (!request.IsVoiceMessage && !request.ForwardedFromMessageId.HasValue
            && string.IsNullOrWhiteSpace(request.Content) && request.Files is not { Count: > 0 })
        {
            return Result<MessageDto>.Failure("Сообщение должно содержать текст или файлы");
        }

        var refCheck = await ValidateReferencesAsync(request);
        if (refCheck.IsFailure) return refCheck.As<MessageDto>();

        var message = new UserMessage
        {
            ChatId = request.ChatId,
            SenderId = senderId,
            Content = request.IsVoiceMessage ? null : request.Content,
            IsDeleted = false,
            ReplyToMessageId = request.ReplyToMessageId,
            ForwardedFromMessageId = request.ForwardedFromMessageId
        };

        _context.UserMessages.Add(message);

        if (request.IsVoiceMessage)
        {
            if (string.IsNullOrEmpty(request.VoiceFileUrl))
                return Result<MessageDto>.Failure("Голосовое сообщение должно содержать аудиофайл");

            message.VoiceMessage = new VoiceMessage
            {
                DurationSeconds = request.VoiceDurationSeconds ?? 0,
                Waveform = request.VoiceWaveform,
                FilePath = StripBaseUrl(request.VoiceFileUrl),
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

        var created = await UserMessagesWithIncludes().AsNoTracking().FirstAsync(m => m.Id == message.Id);
        var senderDto = created.ToDto(senderId, urlBuilder);

        await BroadcastToMembersAsync(created, message.ChatId, "ReceiveMessageDto");
        await NotifyAndUpdateUnreadAsync(senderDto);
        LogMessageCreated(message.Id, message.ChatId);

        return Result<MessageDto>.Success(senderDto);
    }

    private async Task<Result> ValidateReferencesAsync(CreateMessageRequest request)
    {
        if (request.ReplyToMessageId.HasValue &&
            !await _context.UserMessages.AnyAsync(m => m.Id == request.ReplyToMessageId.Value
                && m.ChatId == request.ChatId && m.IsDeleted != true))
        {
            return Result.NotFound("Сообщение для ответа не найдено в этом чате");
        }

        if (request.ForwardedFromMessageId.HasValue &&
            !await _context.UserMessages.AnyAsync(m => m.Id == request.ForwardedFromMessageId.Value
                && m.IsDeleted != true))
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
        if (access!.IsFailure) return access.As<PagedMessagesDto>();

        var (np, nps) = NormalizePagination(page, pageSize, _settings.MaxPageSize);
        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var totalQ = _context.Messages.Where(m => m.ChatId == chatId && m.IsDeleted != true);
        if (cutoff.HasValue) totalQ = totalQ.Where(m => m.CreatedAt >= cutoff.Value);
        var total = await totalQ.CountAsync();

        var skip = (np - 1) * nps;
        var messages = await LoadAllMessagesAsync(chatId, cutoff: cutoff, take: null, oldestFirst: false);
        var paged = messages.Skip(skip).Take(nps).ToList();

        return Result<PagedMessagesDto>.Success(new PagedMessagesDto
        {
            Messages = [.. paged.Select(m => m.ToDto(userId, urlBuilder)).Reverse()],
            CurrentPage = np,
            TotalCount = total,
            HasMoreMessages = total > skip + nps,
            HasNewerMessages = false
        });
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access!.IsFailure) return access.As<PagedMessagesDto>();

        var half = count / 2;
        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var before = await LoadAllMessagesAsync(chatId, beforeId: messageId, cutoff: cutoff, take: half + 1, oldestFirst: false);
        var after = await LoadAllMessagesAsync(chatId, afterId: messageId, cutoff: cutoff, take: half, oldestFirst: true);

        var msgs = before.OrderBy(m => m.Id).Concat(after).Select(m => m.ToDto(userId, urlBuilder)).ToList();

        var oldestId = before.Count > 0 ? before.Min(m => m.Id) : messageId;
        var newestId = after.Count > 0 ? after.Max(m => m.Id) : messageId;

        var hasOlderQ = _context.Messages.Where(m => m.ChatId == chatId && m.Id < oldestId && m.IsDeleted != true);
        var hasNewerQ = _context.Messages.Where(m => m.ChatId == chatId && m.Id > newestId && m.IsDeleted != true);

        if (cutoff.HasValue)
        {
            hasOlderQ = hasOlderQ.Where(m => m.CreatedAt >= cutoff.Value);
            hasNewerQ = hasNewerQ.Where(m => m.CreatedAt >= cutoff.Value);
        }

        return Result<PagedMessagesDto>.Success(BuildPagedResult(msgs, await hasOlderQ.AnyAsync(), await hasNewerQ.AnyAsync()));
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access!.IsFailure) return access.As<PagedMessagesDto>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        var query = UserMessagesLight().Where(m => m.ChatId == chatId && m.Id < messageId && m.IsDeleted != true);

        if (cutoff.HasValue)
            query = query.Where(m => m.CreatedAt >= cutoff.Value);

        var messages = await query.OrderByDescending(m => m.Id).Take(count).ToListAsync();
        var oldestId = messages.Count > 0 ? messages.Min(m => m.Id) : messageId;

        var hasOlderQuery = _context.Messages.Where(m => m.ChatId == chatId && m.Id < oldestId && m.IsDeleted != true);
        if (cutoff.HasValue)
            hasOlderQuery = hasOlderQuery.Where(m => m.CreatedAt >= cutoff.Value);

        return Result<PagedMessagesDto>.Success(BuildPagedResult([.. messages.OrderBy(m => m.Id).Select(m => m.ToDto(userId, urlBuilder))], await hasOlderQuery.AnyAsync(), hasNewer: true));
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access!.IsFailure) return access.As<PagedMessagesDto>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        var query = UserMessagesLight().Where(m => m.ChatId == chatId && m.Id > messageId && m.IsDeleted != true);

        if (cutoff.HasValue)
            query = query.Where(m => m.CreatedAt >= cutoff.Value);

        var messages = await query.OrderBy(m => m.Id).Take(count).ToListAsync();
        var newestId = messages.Count > 0 ? messages.Max(m => m.Id) : messageId;

        var hasNewerQuery = _context.Messages.Where(m => m.ChatId == chatId && m.Id > newestId && m.IsDeleted != true);
        if (cutoff.HasValue)
            hasNewerQuery = hasNewerQuery.Where(m => m.CreatedAt >= cutoff.Value);

        return Result<PagedMessagesDto>.Success(BuildPagedResult([.. messages.Select(m => m.ToDto(userId, urlBuilder))], hasOlder: true, await hasNewerQuery.AnyAsync()));
    }

    #endregion

    #region Update

    public async Task<Result<MessageDto>> UpdateMessageAsync(int messageId, int userId, UpdateMessageDto dto)
    {
        var message = await UserMessagesWithIncludes().FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null) return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access!.IsFailure) return access.As<MessageDto>();

        if (message.SenderId != userId)
            return Result<MessageDto>.Forbidden("Вы можете изменять только свои сообщения");

        var error = message switch
        {
            { IsDeleted: true } => "Сообщение уже удалено",
            _ when message.Poll != null => "Нельзя редактировать сообщение с опросом",
            { IsVoiceMessage: true } => "Нельзя редактировать голосовое сообщение",
            { ForwardedFromMessageId: not null } => "Нельзя редактировать пересланное сообщение",
            _ when string.IsNullOrWhiteSpace(dto.Content) => "Содержимое сообщения не может быть пустым",
            _ => null
        };
        if (error != null) return Result<MessageDto>.Failure(error);

        message.Content = dto.Content!.Trim();
        message.EditedAt = appDateTime.UtcNow;

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
        var message = await _context.UserMessages.Include(m => m.VoiceMessage).Include(m => m.MessageFiles).FirstOrDefaultAsync(m => m.Id == messageId);

        if (message is null)
            return Result.NotFound($"Сообщение с ID {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access!.IsFailure) return access;

        if (message.SenderId != userId && !await accessControl.IsAdminAsync(userId, message.ChatId))
            return Result.Forbidden("Вы можете удалять только свои сообщения");

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

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save;

        await hubNotifier.SendToChatAsync(message.ChatId, "MessageDeleted", new { MessageId = messageId, message.ChatId });
        LogMessageDeleted(messageId);

        return Result.Success();
    }

    #endregion

    #region Pinned Messages

    public async Task<Result<MessageDto>> PinMessageAsync(int messageId, int userId)
    {
        var message = await UserMessagesWithIncludes().FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null) return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access!.IsFailure) return access.As<MessageDto>();

        if (message.IsDeleted == true)
            return Result<MessageDto>.Failure("Нельзя закрепить удаленное сообщение");

        message.PinnedAt = appDateTime.UtcNow;
        message.PinnedByUserId = userId;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<MessageDto>();

        await BroadcastToMembersAsync(message, message.ChatId, "MessageUpdated");
        return Result<MessageDto>.Success(message.ToDto(userId, urlBuilder));
    }

    public async Task<Result<MessageDto>> UnpinMessageAsync(int messageId, int userId)
    {
        var message = await UserMessagesWithIncludes().FirstOrDefaultAsync(m => m.Id == messageId);
        if (message is null) return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access!.IsFailure) return access.As<MessageDto>();

        if (message.PinnedAt == null)
            return Result<MessageDto>.Failure("Сообщение уже не закреплено");

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
        if (access!.IsFailure) return access.As<List<MessageDto>>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var query = UserMessagesLight().Where(m => m.ChatId == chatId && m.PinnedAt != null && m.IsDeleted != true);

        if (cutoff.HasValue)
            query = query.Where(m => m.CreatedAt >= cutoff.Value);

        var pinned = await query.OrderByDescending(m => m.PinnedAt).ToListAsync();
        return Result<List<MessageDto>>.Success([.. pinned.Select(m => m.ToDto(userId, urlBuilder))]);
    }

    #endregion

    #region Search

    public async Task<Result<SearchMessagesResponseDto>> SearchMessagesAsync(int chatId, int userId, SearchMessagesQueryDto query)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access!.IsFailure) return access.As<SearchMessagesResponseDto>();

        var (np, nps) = NormalizePagination(query.Page, query.PageSize, _settings.MaxPageSize);
        var hasQuery = !string.IsNullOrWhiteSpace(query.Query);
        var escaped = hasQuery ? EscapeLikePattern(query.Query) : string.Empty;

        var q = UserMessagesWithIncludes().Where(m => m.ChatId == chatId && m.IsDeleted != true
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

    private async Task<(List<GlobalSearchMessageDto>, int Total, bool HasMore)> SearchMessagesGlobalAsync(List<int> chatIds,
        string escapedQuery, int userId, GlobalSearchQueryDto query, int page, int pageSize, bool hasQuery)
    {
        var q = _context.UserMessages.Where(m => chatIds.Contains(m.ChatId) && m.IsDeleted != true
            && (!hasQuery || (m.Content != null && EF.Functions.ILike(m.Content, $"%{escapedQuery}%"))))
            .Include(m => m.Sender).Include(m => m.Chat).Include(m => m.MessageFiles).Include(m => m.VoiceMessage).Include(m => m.Poll).AsNoTracking();

        q = ApplyMessageSearchFilters(q, query.SenderId, query.DateFrom, query.DateTo,
            query.HasFiles == true, query.HasVoice == true, query.HasPoll == true, query.OnlyText == true);

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
            chatIds = [.. chatIds.Where(id => id == query.FilterChatId.Value)];

        var chats = (hasQuery && query.FilterChatId == null)
            ? await SearchChatsAsync(chatIds, escaped, userId, hasQuery)
            : [];

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

        var dialogs = await _context.Chats.Where(c => chatIds.Contains(c.Id) && c.Type == ChatType.Contact).Include(c => c.ChatMembers).ThenInclude(cm => cm.User)
            .AsNoTracking().ToListAsync();

        foreach (var chat in dialogs)
        {
            var partner = chat.ChatMembers.FirstOrDefault(cm => cm.UserId != userId)?.User;
            if (partner is null) continue;

            var name = partner.GetDisplayName();
            if (!hasQuery || name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (partner.Username ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
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

        var groups = await _context.Chats.Where(c => chatIds.Contains(c.Id) && c.Type != ChatType.Contact
            && (!hasQuery || EF.Functions.ILike(c.Name ?? "", $"%{query}%"))).Take(max).AsNoTracking().ToListAsync();

        result.AddRange(groups.Select(c => c.ToDto(urlBuilder)));
        return [.. result.Take(max)];
    }

    private async Task<Dictionary<int, (string Name, string? Avatar)>> GetDialogPartnersAsync(List<int> chatIds, int userId)
    {
        if (chatIds.Count == 0) return [];

        return (await _context.ChatMembers.Where(cm => chatIds.Contains(cm.ChatId) && cm.UserId != userId).Include(cm => cm.User)
            .AsNoTracking().ToListAsync()).Where(p => p.User != null).ToDictionary(p => p.ChatId, p => (p.User!.GetDisplayName(), urlBuilder.BuildUrl(p.User.Avatar)));
    }

    private GlobalSearchMessageDto BuildSearchDto(UserMessage m, string term, Dictionary<int, (string Name, string? Avatar)> partners)
    {
        var dto = new GlobalSearchMessageDto
        {
            Id = m.Id,
            ChatId = m.ChatId,
            ChatType = m.Chat.Type,
            SenderId = m.SenderId,
            SenderName = m.Sender?.GetDisplayName(),
            Content = m.Content,
            CreatedAt = m.CreatedAt,
            HighlightedContent = Highlight(m.Content, term),
            HasFiles = m.MessageFiles?.Count > 0,
            HasVoice = m.VoiceMessage != null,
            HasPoll = m.Poll != null
        };

        (dto.ChatName, dto.ChatAvatar) = m.Chat.Type == ChatType.Contact && partners.TryGetValue(m.ChatId, out var p)
            ? p
            : (m.Chat.Name, urlBuilder.BuildUrl(m.Chat.Avatar));

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
            await hubNotifier.SendToUserAsync(memberId, hubMethod, message.ToDto(memberId, urlBuilder));
    }

    private async Task MarkChatUpdatedAsync(int chatId)
        => (await _context.Chats.FindAsync(chatId))?.LastMessageTime = appDateTime.UtcNow;

    private async Task NotifyAndUpdateUnreadAsync(MessageDto message)
    {
        try
        {
            var mentionedUsernames = ExtractMentionedUsernames(message.Content);
            var members = await _context.ChatMembers.Where(cm => cm.ChatId == message.ChatId &&
            (!message.SenderId.HasValue || cm.UserId != message.SenderId.Value)).Select(cm => new
            {
                cm.UserId,
                cm.User.Username,
                cm.NotificationsEnabled,
                GlobalEnabled = cm.User.UserSetting == null || cm.User.UserSetting.NotificationsEnabled
            }).ToListAsync();

            foreach (var m in members)
            {
                var unread = await readReceiptService.GetUnreadCountAsync(m.UserId, message.ChatId);
                await hubNotifier.SendToUserAsync(m.UserId, "UnreadCountUpdated",
                    message.ChatId, unread.IsSuccess ? unread.Value : 0);

                if (!m.GlobalEnabled) continue;

                var isMentioned = !string.IsNullOrWhiteSpace(m.Username)
                    && mentionedUsernames.Contains(m.Username!);

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

    private async Task<DateTime?> GetHistoryCutoffAsync(int chatId, int userId)
    {
        var chat = await _context.Chats.AsNoTracking().Select(c => new { c.Id, c.ShowHistoryForNewMembers }).FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat?.ShowHistoryForNewMembers != false)
            return null;

        var role = await accessControl.GetRoleAsync(userId, chatId);
        if (role is ChatRole.Owner or ChatRole.Admin)
            return null;

        var member = await accessControl.GetChatMemberAsync(userId, chatId);
        return member?.JoinedAt ?? DateTime.MinValue;
    }

    private async Task<IQueryable<UserMessage>> ApplyGlobalHistoryFilter(IQueryable<UserMessage> query, List<int> chatIds, int userId)
    {
        var hiddenChats = await _context.Chats.Where(c => chatIds.Contains(c.Id) && !c.ShowHistoryForNewMembers).Select(c => c.Id).ToListAsync();

        if (hiddenChats.Count == 0) return query;

        var memberJoinDates = await _context.ChatMembers.Where(cm => hiddenChats.Contains(cm.ChatId)
            && cm.UserId == userId && cm.Role != ChatRole.Owner && cm.Role != ChatRole.Admin)
            .Select(cm => new { cm.ChatId, cm.JoinedAt }).ToListAsync();

        if (memberJoinDates.Count == 0) return query;

        var joinMap = memberJoinDates.ToDictionary(m => m.ChatId, m => m.JoinedAt);
        var restrictedIds = joinMap.Keys.ToList();

        return query.Where(m => !restrictedIds.Contains(m.ChatId) || m.CreatedAt >= joinMap[m.ChatId]);
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