using API.Application.Bundles;
using API.Application.Configuration;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Dto.Chat;
using Shared.Dto.Message;
using Shared.Dto.Search;
using Shared.DTO.Message;
using Shared.Enum;
using Shared.Hubs;
using System.Text.RegularExpressions;

namespace API.Services.Messaging;

public partial class MessageService(
    IUnitOfWork unitOfWork,
    IChatRepository chatRepository,
    IMessageRepository messageRepository,
    ChatBundle chat,
    MediaBundle media,
    UrlBundle url,
    IReadReceiptService readReceiptService,
    ISystemMessageService systemMessageService,
    IMemoryCache cache,
    IOptions<MessengerSettings> settings,
    ILogger<MessageService> logger) : BaseService<MessageService>(unitOfWork, logger), IMessageService
{
    private readonly IAccessControlService _accessControl = chat.Cache.AccessControl;
    private readonly IHubNotifier _hubNotifier = chat.Notifications.HubNotifier;
    private readonly INotificationService _notificationService = chat.Notifications.NotificationService;
    private readonly IUrlBuilder _urlBuilder = url.UrlBuilder;
    private readonly IFileService _fileService = media.FileService;
    private readonly MessengerSettings _settings = settings.Value;
    private readonly AppDateTime _appDateTime = chat.Time.AppDateTime;
    private readonly ISystemMessageService _systemMessageService = systemMessageService;
    private readonly IMemoryCache _cache = cache;

    [GeneratedRegex(@"(?<![a-z0-9_])@([a-z0-9_]{3,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    #region Helpers

    private async Task<Result> EnsureAccessAsync(int userId, int chatId)
        => await _accessControl.EnsureMemberOfAsync(userId, chatId);

    private string StripBaseUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        var baseUrl = _urlBuilder.BuildUrl("/");
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
        HasNewerMessages = hasNewer
    };

    private static string EscapeLikePattern(string pattern)
        => string.IsNullOrEmpty(pattern) ? pattern : pattern.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private async Task<DateTime?> GetHistoryCutoffAsync(int chatId, int userId)
    {
        var showHistory = await chatRepository.GetShowHistoryForNewMembersAsync(chatId);
        if (showHistory != false)
            return null;

        var role = await _accessControl.GetRoleAsync(userId, chatId);
        if (role is ChatRole.Owner or ChatRole.Admin)
            return null;

        var member = await _accessControl.GetChatMemberAsync(userId, chatId);
        return member?.JoinedAt ?? DateTime.MinValue;
    }

    #endregion

    #region Create

    public async Task<Result<MessageDto>> CreateMessageAsync(int senderId, CreateMessageRequest request)
    {
        var access = await EnsureAccessAsync(senderId, request.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (!request.IsVoiceMessage && !request.ForwardedFromMessageId.HasValue && string.IsNullOrWhiteSpace(request.Content)
            && request.Files is not { Count: > 0 })
        {
            return Result<MessageDto>.Failure("Сообщение должно содержать текст или файлы");
        }

        var refCheck = await ValidateReferencesAsync(request);
        if (refCheck.IsFailure) return refCheck.As<MessageDto>();

        var normalizedForwardedFromMessageId = request.ForwardedFromMessageId;
        if (request.ForwardedFromMessageId.HasValue)
            normalizedForwardedFromMessageId = await ResolveRootForwardedMessageIdAsync(request.ForwardedFromMessageId.Value);

        var content = request.IsVoiceMessage ? null : request.Content;
        if (normalizedForwardedFromMessageId.HasValue && string.IsNullOrWhiteSpace(content))
        {
            var forwarded = await messageRepository.FindUserMessageByIdAsync(normalizedForwardedFromMessageId.Value);
            if (forwarded is not null && !string.IsNullOrWhiteSpace(forwarded.Content))
                content = forwarded.Content;
        }

        var message = new UserMessage
        {
            ChatId = request.ChatId,
            SenderId = senderId,
            Content = content,
            IsDeleted = false,
            ReplyToMessageId = request.ReplyToMessageId,
            ForwardedFromMessageId = normalizedForwardedFromMessageId
        };

        messageRepository.Add(message);

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

        await chatRepository.UpdateLastMessageTimeAsync(request.ChatId, _appDateTime.UtcNow);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<MessageDto>();

        var created = await messageRepository.FindUserMessageWithIncludesAsync(message.Id);
        if (created is null)
            return Result<MessageDto>.Internal("Не удалось загрузить созданное сообщение");

        var senderDto = created.ToDto(senderId, _urlBuilder);

        await BroadcastToMembersAsync(created, message.ChatId, HubMethods.Chat.ReceiveMessage);
        await NotifyAndUpdateUnreadAsync(senderDto);

        LogMessageCreated(message.Id, message.ChatId);

        return Result<MessageDto>.Success(senderDto);
    }

    private async Task<Result> ValidateReferencesAsync(CreateMessageRequest request)
    {
        if (request.ReplyToMessageId.HasValue && !await messageRepository.ExistsInChatAsync(request.ReplyToMessageId.Value, request.ChatId))
        {
            return Result.NotFound("Сообщение для ответа не найдено в этом чате");
        }

        if (request.ForwardedFromMessageId.HasValue && !await messageRepository.ExistsAsync(request.ForwardedFromMessageId.Value))
        {
            return Result.NotFound("Оригинальное сообщение для пересылки не найдено");
        }

        return Result.Success();
    }

    #endregion

    private async Task<int?> ResolveRootForwardedMessageIdAsync(int forwardedFromMessageId)
    {
        var visited = new HashSet<int>();
        int? currentId = forwardedFromMessageId;

        while (currentId.HasValue && visited.Add(currentId.Value))
        {
            var current = await messageRepository.FindUserMessageByIdAsync(currentId.Value);
            if (current?.ForwardedFromMessageId.HasValue != true)
                return current?.Id ?? forwardedFromMessageId;

            currentId = current.ForwardedFromMessageId.Value;
        }

        return forwardedFromMessageId;
    }

    #region Get Messages

    public async Task<Result<PagedMessagesDto>> GetLatestMessagesAsync(int chatId, int userId, int take)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var normalizedTake = Math.Clamp(take, 1, _settings.MaxPageSize);
        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var cacheKey = $"latest_msg_{chatId}_{normalizedTake}_{cutoff?.Ticks ?? 0}";

        if (_cache.TryGetValue(cacheKey, out PagedMessagesDto? cached) && cached is not null)
            return Result<PagedMessagesDto>.Success(cached);

        var (messages, hasOlder) = await messageRepository.GetLatestAsync(chatId, normalizedTake, cutoff);

        var dtos = messages.OrderBy(m => m.Id).Select(m => m.ToDto(userId, _urlBuilder)).ToList();
        var result = BuildPagedResult(dtos, hasOlder, hasNewer: false);

        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(1));

        return Result<PagedMessagesDto>.Success(result);
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesAroundAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var half = count / 2;
        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var before = await messageRepository.GetBeforeAsync(chatId, messageId, half + 1, cutoff);
        var after = await messageRepository.GetAfterAsync(chatId, messageId, half, cutoff);
        var anchor = await messageRepository.FindUserMessageWithIncludesNoTrackingAsync(messageId);

        var allMessages = before.Concat(after);

        if (anchor != null)
            allMessages = allMessages.Append(anchor);

        var ordered = allMessages
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .OrderBy(m => m.Id)
            .ToList();

        var anchorIdx = ordered.FindIndex(m => m.Id == messageId);
        List<Message> window;

        if (anchorIdx < 0)
        {
            window = [.. ordered.Take(count)];
        }
        else
        {
            var start = Math.Max(0, anchorIdx - half);
            var end = Math.Min(ordered.Count, anchorIdx + half + 1);
            window = ordered[start..end];
        }

        var oldestId = window.Count > 0 ? window[0].Id : messageId;
        var newestId = window.Count > 0 ? window[^1].Id : messageId;

        var hasOlder = await messageRepository.HasOlderAsync(chatId, oldestId, cutoff);
        var hasNewer = await messageRepository.HasNewerAsync(chatId, newestId, cutoff);

        var dtos = window.ConvertAll(m => m.ToDto(userId, _urlBuilder));
        return Result<PagedMessagesDto>.Success(BuildPagedResult(dtos, hasOlder, hasNewer));
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesBeforeAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var messages = await messageRepository.GetBeforeAsync(chatId, messageId, count, cutoff);
        var oldestId = messages.Count > 0 ? messages.Min(m => m.Id) : messageId;
        var newestId = messages.Count > 0 ? messages.Max(m => m.Id) : messageId;
        var hasOlder = await messageRepository.HasOlderAsync(chatId, oldestId, cutoff);
        var hasNewer = await messageRepository.HasNewerAsync(chatId, newestId, cutoff);

        return Result<PagedMessagesDto>.Success(
            BuildPagedResult([.. messages.OrderBy(m => m.Id).Select(m => m.ToDto(userId, _urlBuilder))], hasOlder, hasNewer));
    }

    public async Task<Result<PagedMessagesDto>> GetMessagesAfterAsync(int chatId, int messageId, int userId, int count)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var messages = await messageRepository.GetAfterAsync(chatId, messageId, count, cutoff);
        var oldestId = messages.Count > 0 ? messages.Min(m => m.Id) : messageId;
        var newestId = messages.Count > 0 ? messages.Max(m => m.Id) : messageId;
        var hasOlder = await messageRepository.HasOlderAsync(chatId, oldestId, cutoff);
        var hasNewer = await messageRepository.HasNewerAsync(chatId, newestId, cutoff);

        return Result<PagedMessagesDto>.Success(BuildPagedResult([.. messages.OrderBy(m => m.Id).Select(m => m.ToDto(userId, _urlBuilder))], hasOlder, hasNewer));
    }

    public async Task<Result<ChatCountsDto>> GetChatCountsAsync(int chatId, int userId)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure)
            return access.As<ChatCountsDto>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        var counts = await messageRepository.GetChatCountsAsync(chatId, cutoff);
        _logger.LogInformation("[GetChatCounts] ChatId={ChatId}, UserId={UserId}, Cutoff={Cutoff} | Media={MediaCount}, Files={FilesCount}, Polls={PollsCount}, Pinned={PinnedCount}",
            chatId, userId, cutoff, counts.MediaCount, counts.FilesCount, counts.PollsCount, counts.PinnedCount);
        return Result<ChatCountsDto>.Success(counts);
    }

    #endregion

    #region Update

    public async Task<Result<MessageDto>> UpdateMessageAsync(int messageId, int userId, UpdateMessageDto dto)
    {
        var message = await messageRepository.FindUserMessageWithIncludesAsync(messageId);
        if (message is null)
            return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

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
        if (error is not null) return Result<MessageDto>.Failure(error);

        message.Content = dto.Content!.Trim();
        message.EditedAt = _appDateTime.UtcNow;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<MessageDto>();

        await BroadcastToMembersAsync(message, message.ChatId, HubMethods.Chat.MessageUpdated);
        LogMessageUpdated(messageId);

        return Result<MessageDto>.Success(message.ToDto(userId, _urlBuilder));
    }

    #endregion

    #region Delete

    public async Task<Result> DeleteMessageAsync(int messageId, int userId)
    {
        var message = await messageRepository.FindUserMessageForDeleteAsync(messageId);
        if (message is null)
            return Result.NotFound($"Сообщение с ID {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access.IsFailure) return access;

        if (message.SenderId != userId && !await _accessControl.IsAdminAsync(userId, message.ChatId))
            return Result.Forbidden("Вы можете удалять только свои сообщения");

        if (message.IsDeleted == true)
            return Result.Failure("Сообщение уже удалено");

        if (message.VoiceMessage != null)
        {
            _fileService.DeleteFile(message.VoiceMessage.FilePath);
            messageRepository.RemoveVoiceMessage(message.VoiceMessage);
            await SaveChangesAsync();
        }

        await messageRepository.SoftDeleteAsync(messageId, _appDateTime.UtcNow);
        await _hubNotifier.SendToChatAsync(message.ChatId, HubMethods.Chat.MessageDeleted, new { MessageId = messageId, message.ChatId });

        LogMessageDeleted(messageId);
        return Result.Success();
    }

    #endregion

    #region Pinned

    public async Task<Result<MessageDto>> PinMessageAsync(int messageId, int userId)
    {
        var message = await messageRepository.FindUserMessageByIdAsync(messageId);
        if (message is null)
            return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (message.IsDeleted == true)
            return Result<MessageDto>.Failure("Нельзя закрепить удаленное сообщение");

        if (message.PinnedAt != null)
            return Result<MessageDto>.Failure("Сообщение уже закреплено");

        await messageRepository.PinAsync(messageId, userId, _appDateTime.UtcNow);

        var updated = await messageRepository.FindUserMessageWithIncludesNoTrackingAsync(messageId);
        if (updated is null)
            return Result<MessageDto>.Internal("Не удалось загрузить сообщение после закрепления");

        await BroadcastToMembersAsync(updated, updated.ChatId, HubMethods.Chat.MessageUpdated);

        await _systemMessageService.CreateAsync(updated.ChatId, userId, SystemEventType.MessagePinned,
            content: updated.Content?.Length > 50 ? updated.Content[..50] + "..." : updated.Content);

        return Result<MessageDto>.Success(updated.ToDto(userId, _urlBuilder));
    }

    public async Task<Result<MessageDto>> UnpinMessageAsync(int messageId, int userId)
    {
        var message = await messageRepository.FindUserMessageByIdAsync(messageId);
        if (message is null)
            return Result<MessageDto>.NotFound($"Сообщение {messageId} не найдено");

        var access = await EnsureAccessAsync(userId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (message.PinnedAt is null)
            return Result<MessageDto>.Failure("Сообщение уже не закреплено");

        await messageRepository.UnpinAsync(messageId);

        var updated = await messageRepository.FindUserMessageWithIncludesNoTrackingAsync(messageId);
        if (updated is null)
            return Result<MessageDto>.Internal("Не удалось загрузить сообщение после открепления");

        await BroadcastToMembersAsync(updated, updated.ChatId, HubMethods.Chat.MessageUpdated);

        await _systemMessageService.CreateAsync(updated.ChatId, userId, SystemEventType.MessageUnpinned);

        return Result<MessageDto>.Success(updated.ToDto(userId, _urlBuilder));
    }

    public async Task<Result<List<MessageDto>>> GetPinnedMessagesAsync(int chatId, int userId)
    {
        var access = await EnsureAccessAsync(userId, chatId);
        if (access.IsFailure) return access.As<List<MessageDto>>();

        var cutoff = await GetHistoryCutoffAsync(chatId, userId);
        var pinned = await messageRepository.GetPinnedAsync(chatId, cutoff);

        return Result<List<MessageDto>>.Success([.. pinned.Select(m => m.ToDto(userId, _urlBuilder))]);
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
        var cutoff = await GetHistoryCutoffAsync(chatId, userId);

        var (items, total) = await messageRepository.SearchInChatAsync(
            chatId, escaped,
            query.SenderId, query.DateFrom, query.DateTo,
            query.HasFiles == true, query.HasVoice == true,
            query.HasPoll == true, query.OnlyText == true,
            query.OldestFirst, np, nps, cutoff);

        var ordered = query.OldestFirst ? items.AsEnumerable() : items.AsEnumerable().Reverse();

        return Result<SearchMessagesResponseDto>.Success(new()
        {
            Messages = [.. ordered.Select(m => m.ToDto(userId, _urlBuilder))],
            TotalCount = total,
            CurrentPage = np,
            HasMoreMessages = total > ((np - 1) * nps) + nps
        });
    }

    public async Task<Result<GlobalSearchResponseDto>> GlobalSearchAsync(int userId, GlobalSearchQueryDto query)
    {
        var (np, nps) = NormalizePagination(query.Page, query.PageSize, 50);
        var hasQuery = !string.IsNullOrWhiteSpace(query.Query);
        var escaped = hasQuery ? EscapeLikePattern(query.Query) : string.Empty;

        var chatIds = await _accessControl.GetUserChatIdsAsync(userId);
        if (chatIds.Count == 0)
        {
            return Result<GlobalSearchResponseDto>.Success(new()
            {
                Chats = [],
                Messages = [],
                CurrentPage = query.Page
            });
        }

        if (query.FilterChatId.HasValue)
            chatIds = [.. chatIds.Where(id => id == query.FilterChatId.Value)];

        var historyFilter = await BuildGlobalHistoryFilterAsync(chatIds, userId);

        var chats = (hasQuery && query.FilterChatId is null) ? await SearchChatsAsync(chatIds, escaped, userId) : [];

        var (msgs, total, hasMore) = await SearchMessagesGlobalAsync(chatIds, escaped, userId, query, np, nps, historyFilter);

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

    private async Task<(List<GlobalSearchMessageDto>, int Total, bool HasMore)> SearchMessagesGlobalAsync(List<int> chatIds,
        string escapedQuery, int userId, GlobalSearchQueryDto query, int page, int pageSize, Dictionary<int, DateTime> historyFilter)
    {
        var (items, total) = await messageRepository.SearchGlobalAsync(
            chatIds, escapedQuery,
            query.SenderId, query.DateFrom, query.DateTo,
            query.HasFiles == true, query.HasVoice == true,
            query.HasPoll == true, query.OnlyText == true,
            query.OldestFirst, page, pageSize,
            historyFilter);

        var dialogIds = items.Where(m => m.Chat.Type == ChatType.Contact).Select(m => m.ChatId).Distinct().ToList();

        var partners = await GetDialogPartnersForSearchAsync(dialogIds, userId);

        return (items.ConvertAll(m => BuildSearchDto(m, escapedQuery, partners)), total, total > ((page - 1) * pageSize) + pageSize);
    }

    private async Task<List<ChatDto>> SearchChatsAsync(List<int> chatIds, string query, int userId)
    {
        const int max = 5;
        var result = new List<ChatDto>();

        var dialogs = await chatRepository.GetContactChatsWithMembersAsync(chatIds);

        foreach (var chatEntity in dialogs)
        {
            var partner = chatEntity.ChatMembers.FirstOrDefault(cm => cm.UserId != userId)?.User;
            if (partner is null) continue;

            var name = partner.GetDisplayName();

            if (string.IsNullOrEmpty(query) || name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (partner.Username ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ChatDto
                {
                    Id = chatEntity.Id,
                    Name = name,
                    Type = chatEntity.Type,
                    Avatar = _urlBuilder.BuildUrl(partner.Avatar),
                    LastMessageDate = chatEntity.LastMessageTime
                });
            }
        }

        var groups = await chatRepository.SearchGroupChatsAsync(chatIds, query, max);
        result.AddRange(groups.Select(c => c.ToDto(_urlBuilder)));

        return [.. result.Take(max)];
    }

    private async Task<Dictionary<int, (string Name, string? Avatar)>> GetDialogPartnersForSearchAsync(List<int> chatIds, int userId)
    {
        if (chatIds.Count == 0) return [];

        var partners = await chatRepository.GetDialogPartnersAsync(chatIds, userId);

        return partners.ToDictionary(p => p.ChatId, p => (FormatName(p.Surname, p.Name, p.Midname), _urlBuilder.BuildUrl(p.Avatar)));
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
                ? p : (m.Chat.Name, _urlBuilder.BuildUrl(m.Chat.Avatar));

        return dto;
    }

    private static string? Highlight(string? content, string term)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(term)) return content;

        var idx = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return content.Length > 100 ? content[..100] + "..." : content;

        const int ctx = 40;
        var start = Math.Max(0, idx - ctx);
        var end = Math.Min(content.Length, idx + term.Length + ctx);
        return (start > 0 ? "..." : "") + content[start..end] + (end < content.Length ? "..." : "");
    }

    private async Task<Dictionary<int, DateTime>> BuildGlobalHistoryFilterAsync(List<int> chatIds, int userId)
        => await chatRepository.GetHistoryRestrictionsAsync(chatIds, userId);

    #endregion

    #region Private Broadcast & Notify

    private async Task BroadcastToMembersAsync(Message message, int chatId, string hubMethod)
    {
        var senderId = message is UserMessage um ? um.SenderId ?? 0 : 0;
        var dto = message.ToDto(senderId, _urlBuilder);

        await _hubNotifier.SendToChatAsync(chatId, hubMethod, dto);
    }

    private async Task NotifyAndUpdateUnreadAsync(MessageDto message)
    {
        try
        {
            var mentionedUsernames = ExtractMentionedUsernames(message.Content);

            var members = await chatRepository.GetMembersForNotificationAsync(message.ChatId, message.SenderId);

            var memberIds = members.ConvertAll(m => m.UserId);
            var unreadCounts = await readReceiptService.GetUnreadCountsForUsersInChatAsync(message.ChatId, memberIds);

            foreach (var m in members)
            {
                var unread = unreadCounts.GetValueOrDefault(m.UserId, 0);

                await _hubNotifier.SendToUserAsync(m.UserId, HubMethods.Chat.UnreadCountUpdated, message.ChatId, unread);

                if (!m.GlobalNotificationsEnabled) continue;

                var isMentioned = !string.IsNullOrWhiteSpace(m.Username)
                    && mentionedUsernames.Contains(m.Username);

                if (isMentioned)
                {
                    await _notificationService.SendMentionNotificationAsync(m.UserId, message);
                    continue;
                }

                if (m.ChatNotificationsEnabled)
                    await _notificationService.SendNotificationAsync(m.UserId, message);
            }
        }
        catch (Exception ex)
        {
            LogNotificationError(message.ChatId, ex);
        }
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

    private static string FormatName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : "Без имени";
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