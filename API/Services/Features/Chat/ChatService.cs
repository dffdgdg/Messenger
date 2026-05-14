using API.Hubs;
using API.Repositories.Abstarctions;
using API.Repositories.Projections;
using API.Services.Base;
using API.Services.Features.Chat;
using API.Services.Infrastructure.Bundles;
using API.Services.Infrastructure.Security;
using Shared.Hubs;

namespace API.Services.Chat;

public partial class ChatService(MessengerDbContext context, IChatRepository chatRepository, IUserRepository userRepository,
    ChatBundle chatBundle, MediaBundle media, PresenceBundle presence, UrlBundle url, IReadReceiptService readReceiptService, IHubContext<ChatHub> hubContext,
    ILogger<ChatService> logger) : BaseService<ChatService>(context, logger), IChatService
{
    private readonly IAccessControlService _accessControl = chatBundle.Cache.AccessControl;
    private readonly IFileService _fileService = media.FileService;
    private readonly IOnlineUserService _onlineService = presence.OnlineService;
    private readonly IUrlBuilder _urlBuilder = url.UrlBuilder;
    private readonly ICacheService _cacheService = chatBundle.Cache.CacheService;
    private readonly ISystemMessageService _systemMessages = chatBundle.SystemMessages;
    private readonly IHubNotifier _hubNotifier = chatBundle.Notifications.HubNotifier;
    private readonly AppDateTime _appDateTime = chatBundle.Time.AppDateTime;

    #region Get Chats

    public async Task<Result<List<ChatDto>>> GetUserChatsAsync(int userId)
    {
        var chatIds = await _accessControl.GetUserChatIdsAsync(userId);
        if (chatIds.Count == 0)
            return Result<List<ChatDto>>.Success([]);

        var chatsData = await LoadChatsWithLastMessageAsync(chatIds);
        var unreadCounts = await readReceiptService.GetUnreadCountsForChatsAsync(userId, chatIds);

        var dialogChatIds = chatsData.Where(c => c.Chat.Type == ChatType.Contact).Select(c => c.Chat.Id).ToList();

        var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId);
        var userRoles = await LoadCurrentUserRolesAsync(chatIds, userId);
        var result = chatsData.ConvertAll(item => BuildChatDto(item, unreadCounts, dialogPartners, userRoles));

        return Result<List<ChatDto>>.Success([.. result.OrderByDescending(c => c.UnreadCount > 0).ThenByDescending(c => c.LastMessageDate)]);
    }

    private async Task<List<ChatWithLastMessage>> LoadChatsWithLastMessageAsync(List<int> chatIds)
    {
        var chats = await chatRepository.GetByIdsAsync(chatIds);

        var lastMessages = await chatRepository.GetLastMessagesAsync(chatIds);
        var lastMsgMap = lastMessages.ToDictionary(m => m.ChatId);

        return chats.ConvertAll(chatEntity =>
        {
            LastMessageInfo? lastMsg = lastMsgMap.TryGetValue(chatEntity.Id, out var msg) ? MapToLastMessageInfo(msg) : null;

            return new ChatWithLastMessage { Chat = chatEntity, LastMessage = lastMsg };
        });
    }

    private async Task<Dictionary<int, ChatRole>> LoadCurrentUserRolesAsync(List<int> chatIds, int userId)
    {
        return await _context.ChatMembers
            .Where(cm => chatIds.Contains(cm.ChatId) && cm.UserId == userId)
            .ToDictionaryAsync(cm => cm.ChatId, cm => cm.Role);
    }

    private static LastMessageInfo MapToLastMessageInfo(LastMessageProjection msg) =>
        new(msg.Id, msg.Content, msg.CreatedAt, msg.IsSystemMessage,
            msg.SenderId, msg.IsVoiceMessage, msg.SystemEventType,
            msg.TargetUserId, msg.SenderName, msg.TargetUserName,
            msg.HasPoll, msg.HasFiles);

    private ChatDto BuildChatDto(ChatWithLastMessage item,
        Dictionary<int, int> unreadCounts,
        Dictionary<int, DialogPartnerInfo> dialogPartners,
        Dictionary<int, ChatRole> userRoles)
    {
        var msg = item.LastMessage;
        var (preview, senderName, isSystem) =
            msg is not null ? BuildLastMessagePreview(msg) : (null, null, false);

        var isContactChat = item.Chat.Type == ChatType.Contact;

        var dto = new ChatDto
        {
            Id = item.Chat.Id,
            Type = item.Chat.Type,
            CreatedById = item.Chat.CreatedById ?? 0,
            LastMessageDate = msg?.CreatedAt ?? item.Chat.LastMessageTime,
            LastMessagePreview = preview,
            LastMessageSenderName = isContactChat ? null : senderName,
            LastMessageSenderId = msg?.SenderId,
            LastMessageIsSystem = isSystem,
            LastMessageIsPoll = msg?.HasPoll ?? false,
            LastMessageIsVoice = msg?.IsVoiceMessage ?? false,
            UnreadCount = unreadCounts.GetValueOrDefault(item.Chat.Id, 0)
        };

        ApplyChatIdentity(dto, item, dialogPartners);
        dto.CurrentUserRole = userRoles.GetValueOrDefault(item.Chat.Id);
        dto.ShowHistoryForNewMembers = item.Chat.ShowHistoryForNewMembers;

        return dto;
    }

    private void ApplyChatIdentity(ChatDto dto, ChatWithLastMessage item, Dictionary<int, DialogPartnerInfo> dialogPartners)
    {
        if (item.Chat.Type == ChatType.Contact &&
            dialogPartners.TryGetValue(item.Chat.Id, out var partner))
        {
            dto.Name = partner.DisplayName;
            dto.Avatar = partner.AvatarUrl;
            dto.ContactUserId = partner.UserId;
            dto.ContactIsOnline = partner.IsOnline;
            dto.ContactStatusType = partner.StatusType;
            dto.ContactStatusExpiresAt = partner.StatusExpiresAt;
        }
        else
        {
            dto.Name = item.Chat.Name;
            dto.Avatar = _urlBuilder.BuildUrl(item.Chat.Avatar);
        }
    }

    private static (string? Preview, string? SenderName, bool IsSystem) BuildLastMessagePreview(LastMessageInfo msg)
    {
        if (msg.IsSystemMessage)
        {
            var formatted = SystemMessageFormatter.Format(
                msg.SystemEventType, msg.SenderName, msg.TargetUserName);
            return (formatted, null, true);
        }

        var preview = BuildContentPreview(msg);
        var firstName = ExtractFirstName(msg.SenderName);
        return (preview, firstName, false);
    }

    private static string? BuildContentPreview(LastMessageInfo msg)
    {
        if (msg.HasPoll)
            return $"📊 {Truncate(msg.Content, 50) ?? "Опрос"}";

        if (msg.IsVoiceMessage)
            return "Голосовое сообщение";

        if (msg.HasFiles && string.IsNullOrWhiteSpace(msg.Content))
            return "Вложение";

        return Truncate(msg.Content, 50);
    }

    private static string? ExtractFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^1];
    }

    public async Task<Result<ChatDto>> GetChatForUserAsync(int chatId, int userId)
    {
        var access = await _accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure) return access.As<ChatDto>();

        var chatEntity = await chatRepository.FindByIdAsync(chatId);
        if (chatEntity is null)
            return Result<ChatDto>.NotFound($"Чат с ID {chatId} не найден");

        var member = await _context.ChatMembers.Where(cm => cm.ChatId == chatId && cm.UserId == userId)
            .Select(cm => cm.Role).FirstOrDefaultAsync();

        var dto = new ChatDto
        {
            Id = chatEntity.Id,
            Type = chatEntity.Type,
            CreatedById = chatEntity.CreatedById ?? 0,
            LastMessageDate = chatEntity.LastMessageTime,
            ShowHistoryForNewMembers = chatEntity.ShowHistoryForNewMembers,
            CurrentUserRole = member
        };

        if (chatEntity.Type == ChatType.Contact)
        {
            var partner = await GetDialogPartnerAsync(chatId, userId);
            if (partner is not null)
            {
                dto.Name = partner.Value.DisplayName;
                dto.Avatar = partner.Value.AvatarUrl;
            }
        }
        else
        {
            dto.Name = chatEntity.Name;
            dto.Avatar = _urlBuilder.BuildUrl(chatEntity.Avatar);
        }

        return Result<ChatDto>.Success(dto);
    }

    public async Task<Result<List<ChatDto>>> GetUserDialogsAsync(int userId)
    {
        var allChatsResult = await GetUserChatsAsync(userId);
        if (allChatsResult.IsFailure) return allChatsResult;

        var dialogs = allChatsResult.Value!.Where(c => c.Type == ChatType.Contact).ToList();

        return Result<List<ChatDto>>.Success(dialogs);
    }

    public async Task<Result<List<ChatDto>>> GetUserGroupsAsync(int userId)
    {
        var allChatsResult = await GetUserChatsAsync(userId);
        if (allChatsResult.IsFailure) return allChatsResult;

        var groups = allChatsResult.Value!.Where(c => c.Type != ChatType.Contact).ToList();

        return Result<List<ChatDto>>.Success(groups);
    }

    public async Task<Result<ChatDto>> GetContactChatAsync(int userId, int contactUserId)
    {
        var chatEntity = await chatRepository.FindContactChatAsync(userId, contactUserId);

        if (chatEntity is null)
            return Result<ChatDto>.NotFound("Диалог не найден");

        var dto = chatEntity.ToDto(_urlBuilder);
        var partner = await GetDialogPartnerAsync(chatEntity.Id, userId);

        if (partner is not null)
        {
            dto.Name = partner.Value.DisplayName;
            dto.Avatar = partner.Value.AvatarUrl;
        }

        return Result<ChatDto>.Success(dto);
    }

    #endregion

    #region Members

    public async Task<Result<List<UserDto>>> GetChatMembersAsync(int chatId, int userId)
    {
        var access = await _accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure) return access.As<List<UserDto>>();

        var members = await chatRepository.GetMembersWithUsersAsync(chatId);

        var memberIds = members.ConvertAll(m => m.UserId);
        var onlineIds = _onlineService.FilterOnline(memberIds);

        var result = members.ConvertAll(m => new UserDto
        {
            Id = m.UserId,
            Username = m.Username,
            DisplayName = FormatName(m.Surname, m.Name, m.Midname),
            Surname = m.Surname,
            Name = m.Name,
            Midname = m.Midname,
            Avatar = _urlBuilder.BuildUrl(m.Avatar),
            IsOnline = onlineIds.Contains(m.UserId),
            LastOnline = m.LastOnline,
            StatusType = m.StatusType,
            StatusExpiresAt = m.StatusExpiresAt
        });

        return Result<List<UserDto>>.Success(result);
    }

    #endregion

    #region CRUD

    public async Task<Result<ChatDto>> CreateChatAsync(ChatDto dto, CancellationToken ct = default)
    {
        if (dto.CreatedById <= 0)
            return Result<ChatDto>.Failure("Некорректный ID создателя");

        var contactValidation = await ValidateContactChatAsync(dto, ct);
        if (contactValidation.IsFailure) return contactValidation.As<ChatDto>();

        var contactUserId = contactValidation.Value;

        if (dto.Type != ChatType.Contact && string.IsNullOrWhiteSpace(dto.Name))
            return Result<ChatDto>.Failure("Название чата обязательно");

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var newChat = await PersistNewChatAsync(dto, contactUserId, ct);
            await transaction.CommitAsync(ct);

            InvalidateCachesAfterCreate(dto.CreatedById, contactUserId);

            if (newChat.Type != ChatType.Contact)
                await _systemMessages.CreateAsync(newChat.Id, dto.CreatedById, SystemEventType.ChatCreated);

            var createdEvent = new ChatUpdateEventDto
            {
                Id = newChat.Id,
                Name = newChat.Name,
                Type = newChat.Type,
                CreatedById = dto.CreatedById,
                ShowHistoryForNewMembers = newChat.ShowHistoryForNewMembers
            };

            var memberIds = await _context.ChatMembers
                .Where(cm => cm.ChatId == newChat.Id)
                .Select(cm => cm.UserId)
                .ToListAsync(ct);

            foreach (var memberId in memberIds)
            {
                await _hubNotifier.SendToUserAsync(memberId, HubMethods.Chat.ChatUpdated, createdEvent);
                foreach (var connectionId in _onlineService.GetConnectionIds(memberId))
                    await hubContext.Groups.AddToGroupAsync(connectionId, $"chat_{newChat.Id}", ct);
            }

            LogChatCreated(newChat.Id, dto.CreatedById);

            return Result<ChatDto>.Success(new ChatDto
            {
                Id = newChat.Id,
                Name = newChat.Name,
                Type = newChat.Type,
                CreatedById = dto.CreatedById,
                ShowHistoryForNewMembers = newChat.ShowHistoryForNewMembers
            });
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<Result<int?>> ValidateContactChatAsync(ChatDto dto, CancellationToken ct)
    {
        if (dto.Type != ChatType.Contact)
            return Result<int?>.Success(null);

        if (!int.TryParse(dto.Name?.Trim(), out var parsedId))
            return Result<int?>.Success(null);

        var contactExists = await userRepository.ExistsAsync(parsedId, ct);
        if (!contactExists)
            return Result<int?>.NotFound("Указанный собеседник не найден");

        var existing = await chatRepository.FindContactChatAsync(dto.CreatedById, parsedId, ct);
        if (existing is not null)
            return Result<int?>.Conflict("Диалог с этим пользователем уже существует");

        return Result<int?>.Success(parsedId);
    }

    private async Task<Data.Chat> PersistNewChatAsync(ChatDto dto, int? contactUserId, CancellationToken ct)
    {
        var newChat = new Data.Chat
        {
            Name = dto.Type == ChatType.Contact ? null : dto.Name?.Trim(),
            Type = dto.Type,
            CreatedById = dto.CreatedById,
            CreatedAt = _appDateTime.UtcNow,
            ShowHistoryForNewMembers = dto.ShowHistoryForNewMembers
        };

        chatRepository.Add(newChat);
        await _context.SaveChangesAsync(ct);

        AddChatMembers(newChat.Id, dto.CreatedById, contactUserId);
        await _context.SaveChangesAsync(ct);

        return newChat;
    }

    private void AddChatMembers(int chatId, int creatorId, int? contactUserId)
    {
        chatRepository.AddMember(new ChatMember
        {
            ChatId = chatId,
            UserId = creatorId,
            Role = ChatRole.Owner,
            JoinedAt = _appDateTime.UtcNow
        });

        if (contactUserId.HasValue && contactUserId.Value != creatorId)
        {
            chatRepository.AddMember(new ChatMember
            {
                ChatId = chatId,
                UserId = contactUserId.Value,
                Role = ChatRole.Member,
                JoinedAt = _appDateTime.UtcNow
            });
        }
    }

    private void InvalidateCachesAfterCreate(int creatorId, int? contactUserId)
    {
        _cacheService.InvalidateUserChats(creatorId);
        if (contactUserId.HasValue)
            _cacheService.InvalidateUserChats(contactUserId.Value);
    }

    public async Task<Result<ChatDto>> UpdateChatAsync(int chatId, int userId, UpdateChatDto dto)
    {
        var admin = await _accessControl.EnsureAdminOfAsync(userId, chatId);
        if (admin.IsFailure) return admin.As<ChatDto>();

        var chatEntity = await chatRepository.FindByIdAsync(chatId);
        if (chatEntity is null)
            return Result<ChatDto>.NotFound($"Чат с ID {chatId} не найден");

        if (chatEntity.Type == ChatType.Contact)
            return Result<ChatDto>.Failure("Нельзя редактировать диалог");

        var applyResult = await ApplyChatUpdatesAsync(chatEntity, dto, userId, chatId);
        if (applyResult.IsFailure) return applyResult.As<ChatDto>();

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<ChatDto>();

        LogChatUpdated(chatId, userId);

        var updateEvent = BuildUpdateEvent(chatEntity);
        await _hubNotifier.SendToChatAsync(chatId, HubMethods.Chat.ChatUpdated, updateEvent);

        return Result<ChatDto>.Success(new ChatDto
        {
            Id = chatEntity.Id,
            Name = chatEntity.Name,
            Type = chatEntity.Type,
            CreatedById = chatEntity.CreatedById ?? 0,
            LastMessageDate = chatEntity.LastMessageTime,
            Avatar = _urlBuilder.BuildUrl(chatEntity.Avatar),
            ShowHistoryForNewMembers = chatEntity.ShowHistoryForNewMembers
        });
    }

    private async Task<Result> ApplyChatUpdatesAsync(Data.Chat chatEntity, UpdateChatDto dto, int userId, int chatId)
    {
        if (!string.IsNullOrWhiteSpace(dto.Name))
            chatEntity.Name = dto.Name.Trim();

        if (dto.ChatType.HasValue)
        {
            if (!await _accessControl.IsOwnerAsync(userId, chatId))
                return Result.Forbidden("Только владелец может изменить тип чата");

            chatEntity.Type = dto.ChatType.Value;
        }

        if (dto.ShowHistoryForNewMembers.HasValue)
            chatEntity.ShowHistoryForNewMembers = dto.ShowHistoryForNewMembers.Value;

        return Result.Success();
    }

    public async Task<Result> DeleteChatAsync(int chatId, int userId)
    {
        var owner = await _accessControl.EnsureOwnerOfAsync(userId, chatId);
        if (owner.IsFailure) return owner;

        var chatEntity = await chatRepository.FindByIdWithMembersAsync(chatId);
        if (chatEntity is null)
            return Result.NotFound($"Чат с ID {chatId} не найден");

        var memberIds = chatEntity.ChatMembers.Select(cm => cm.UserId).ToList();

        var voicePaths = await chatRepository.GetVoiceFilePathsAsync(chatId);
        foreach (var path in voicePaths)
            _fileService.DeleteFile(path);

        chatRepository.Remove(chatEntity);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save;

        foreach (var memberId in memberIds)
        {
            _cacheService.InvalidateUserChats(memberId);
            _cacheService.InvalidateMembership(memberId, chatId);
            await _hubNotifier.SendToUserAsync(memberId, HubMethods.Chat.ChatRemoved, chatId);
        }

        LogChatDeleted(chatId, userId);
        return Result.Success();
    }

    public async Task<Result> RemoveChatAvatarAsync(int chatId, int userId)
    {
        var admin = await _accessControl.EnsureAdminOfAsync(userId, chatId);
        if (admin.IsFailure) return admin;

        var chatEntity = await chatRepository.FindByIdAsync(chatId);
        if (chatEntity is null)
            return Result.NotFound($"Чат с ID {chatId} не найден");

        if (chatEntity.Type == ChatType.Contact)
            return Result.Failure("Нельзя удалить аватар у диалога");

        if (!string.IsNullOrEmpty(chatEntity.Avatar))
            _fileService.DeleteFile(chatEntity.Avatar);

        chatEntity.Avatar = null;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save;

        LogAvatarRemoved(chatId);

        await _hubNotifier.SendToChatAsync(chatId, HubMethods.Chat.ChatUpdated, BuildUpdateEvent(chatEntity));

        return Result.Success();
    }

    public async Task<Result<string>> UploadChatAvatarAsync(int chatId, int userId, IFormFile file)
    {
        var admin = await _accessControl.EnsureAdminOfAsync(userId, chatId);
        if (admin.IsFailure) return admin.As<string>();

        if (file is null || file.Length == 0)
            return Result<string>.Failure("Файл не загружен");

        if (!file.ContentType.StartsWith("image/"))
            return Result<string>.Failure("Файл должен быть изображением");

        var chatEntity = await chatRepository.FindByIdAsync(chatId);
        if (chatEntity is null)
            return Result<string>.NotFound($"Чат с ID {chatId} не найден");

        if (chatEntity.Type == ChatType.Contact)
            return Result<string>.Failure("Нельзя установить аватар для диалога");

        var saveResult = await _fileService.SaveImageAsync(file, "chats", chatEntity.Avatar);
        if (saveResult.IsFailure) return saveResult.As<string>();

        chatEntity.Avatar = saveResult.Value;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<string>();

        await _systemMessages.CreateAsync(chatId, userId, SystemEventType.ChatAvatarUpdated);

        LogAvatarUploaded(chatId);

        var avatarUrl = _urlBuilder.BuildUrl(saveResult.Value)!;

        await _hubNotifier.SendToChatAsync(chatId, HubMethods.Chat.ChatUpdated, BuildUpdateEvent(chatEntity));

        return Result<string>.Success(avatarUrl);
    }

    #endregion

    #region Private Helpers

    private ChatUpdateEventDto BuildUpdateEvent(Data.Chat chatEntity) => new()
    {
        Id = chatEntity.Id,
        Name = chatEntity.Name,
        Type = chatEntity.Type,
        CreatedById = chatEntity.CreatedById ?? 0,
        Avatar = _urlBuilder.BuildUrl(chatEntity.Avatar),
        ShowHistoryForNewMembers = chatEntity.ShowHistoryForNewMembers
    };

    private async Task<DialogPartnerInfo?> GetDialogPartnerAsync(int chatId, int currentUserId)
        => (await GetDialogPartnersAsync([chatId], currentUserId)).GetValueOrDefault(chatId);

    private async Task<Dictionary<int, DialogPartnerInfo>> GetDialogPartnersAsync(List<int> chatIds, int currentUserId)
    {
        if (chatIds.Count == 0) return [];

        var partners = await chatRepository.GetDialogPartnersAsync(chatIds, currentUserId);

        var partnerUserIds = partners.ConvertAll(p => p.UserId);
        var onlineIds = _onlineService.FilterOnline(partnerUserIds);

        return partners.ToDictionary(p => p.ChatId, p => new DialogPartnerInfo
        {
            UserId = p.UserId,
            DisplayName = FormatName(p.Surname, p.Name, p.Midname),
            AvatarUrl = _urlBuilder.BuildUrl(p.Avatar),
            StatusType = p.StatusType,
            StatusExpiresAt = p.StatusExpiresAt,
            IsOnline = onlineIds.Contains(p.UserId)
        });
    }

    private static string FormatName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : "Без имени";
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    #endregion

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "Чат {ChatId} создан пользователем {UserId}")]
    private partial void LogChatCreated(int chatId, int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Чат {ChatId} обновлён пользователем {UserId}")]
    private partial void LogChatUpdated(int chatId, int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Чат {ChatId} удалён пользователем {UserId}")]
    private partial void LogChatDeleted(int chatId, int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Аватар загружен для чата {ChatId}")]
    private partial void LogAvatarUploaded(int chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Аватар удалён для чата {ChatId}")]
    private partial void LogAvatarRemoved(int chatId);

    #endregion

    #region Inner Types

    private sealed record LastMessageInfo(int Id, string? Content, DateTime CreatedAt, bool IsSystemMessage,
        int? SenderId, bool IsVoiceMessage, SystemEventType? SystemEventType,
        int? TargetUserId, string? SenderName, string? TargetUserName,
        bool HasPoll, bool HasFiles);

    private sealed record ChatWithLastMessage
    {
        public Data.Chat Chat { get; init; } = null!;
        public LastMessageInfo? LastMessage { get; init; }
    }

    private readonly record struct DialogPartnerInfo
    {
        public int UserId { get; init; }
        public string DisplayName { get; init; }
        public string? AvatarUrl { get; init; }
        public UserStatusType StatusType { get; init; }
        public DateTime? StatusExpiresAt { get; init; }
        public bool IsOnline { get; init; }
    }

    #endregion
}