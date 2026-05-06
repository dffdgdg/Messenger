using API.Services.Base;
using API.Services.Features.Chat;
using API.Services.Infrastructure.Bundles;
using API.Services.Infrastructure.Security;

namespace API.Services.Chat;

public partial class ChatService(MessengerDbContext context, ChatBundle chat, MediaBundle media, PresenceBundle presence,
    UrlBundle url, IReadReceiptService readReceiptService, ILogger<ChatService> logger) : BaseService<ChatService>(context, logger), IChatService
{
    private readonly IAccessControlService _accessControl = chat.Cache.AccessControl;
    private readonly IFileService _fileService = media.FileService;
    private readonly IOnlineUserService _onlineService = presence.OnlineService;
    private readonly IUrlBuilder _urlBuilder = url.UrlBuilder;
    private readonly ICacheService _cacheService = chat.Cache.CacheService;
    private readonly ISystemMessageService _systemMessages = chat.SystemMessages;
    private readonly IHubNotifier _hubNotifier = chat.Notifications.HubNotifier;
    private readonly AppDateTime _appDateTime = chat.Time.AppDateTime;

    #region Get Chats

    public async Task<Result<List<ChatDto>>> GetUserChatsAsync(int userId)
    {
        var chatIds = await _accessControl.GetUserChatIdsAsync(userId);
        if (chatIds.Count == 0)
            return Result<List<ChatDto>>.Success([]);

        var chatsData = await LoadChatsWithLastMessageAsync(chatIds);
        var unreadCounts = await readReceiptService.GetUnreadCountsForChatsAsync(userId, chatIds);

        var dialogChatIds = chatsData.Where(c => c.Chat.Type == ChatType.Contact)
                                       .Select(c => c.Chat.Id).ToList();
        var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId);

        var result = chatsData.ConvertAll(item => BuildChatDto(item, unreadCounts, dialogPartners));

        return Result<List<ChatDto>>.Success([.. result.OrderByDescending(c => c.UnreadCount > 0).ThenByDescending(c => c.LastMessageDate)]);
    }


    private async Task<List<ChatWithLastMessage>> LoadChatsWithLastMessageAsync(List<int> chatIds)
    {
        var chats = await LoadChatsAsync(chatIds);
        var lastMessages = await LoadLastMessagesAsync(chatIds);
        var lastMsgMap = lastMessages.ToDictionary(m => m.ChatId);

        return chats.ConvertAll(chat =>
        {
            LastMessageInfo? lastMsg = lastMsgMap.TryGetValue(chat.Id, out var msg)
                ? MapToLastMessageInfo(msg)
                : null;

            return new ChatWithLastMessage { Chat = chat, LastMessage = lastMsg };
        });
    }

    private Task<List<Data.Chat>> LoadChatsAsync(List<int> chatIds)
        => _context.Chats.Where(c => chatIds.Contains(c.Id)).AsNoTracking().ToListAsync();

    private async Task<List<RawLastMessage>> LoadLastMessagesAsync(List<int> chatIds)
    {
        var lastMessageIds = await _context.Messages.Where(m => chatIds.Contains(m.ChatId) && m.IsDeleted != true)
            .GroupBy(m => m.ChatId).Select(g => g.Max(m => m.Id)).ToListAsync();

        if (lastMessageIds.Count == 0)
            return [];

        return await _context.Messages.Where(m => lastMessageIds.Contains(m.Id)).Select(m => new RawLastMessage
        {
            Id = m.Id,
            ChatId = m.ChatId,
            CreatedAt = m.CreatedAt,
            IsSystemMessage = m is SystemMessage,
            SenderId = m is UserMessage ? ((UserMessage)m).SenderId : ((SystemMessage)m).InitiatorId,
            Content = m is UserMessage ? ((UserMessage)m).Content : ((SystemMessage)m).Content,
            SystemEventType = m is SystemMessage ? ((SystemMessage)m).SystemEventType : null,
            TargetUserId = m is SystemMessage ? ((SystemMessage)m).TargetUserId : null,
            SenderName = BuildSenderName(m),
            TargetUserName = BuildTargetUserName(m),
            IsVoiceMessage = m is UserMessage && ((UserMessage)m).VoiceMessage != null,
            HasPoll = m is UserMessage && ((UserMessage)m).Poll != null,
            HasFiles = m is UserMessage && ((UserMessage)m).MessageFiles.Any()
        }).ToListAsync();
    }

    private static string? BuildSenderName(Message m)
    {
        if (m is UserMessage um)
            return BuildUserFullName(um.Sender.Surname, um.Sender.Name, um.Sender.Midname);

        if (m is SystemMessage sm && sm.Initiator != null)
            return BuildUserFullName(sm.Initiator.Surname, sm.Initiator.Name, sm.Initiator.Midname);

        return null;
    }

    private static string? BuildTargetUserName(Message m)
    {
        if (m is not SystemMessage sm || sm.TargetUser is null)
            return null;

        return BuildUserFullName(sm.TargetUser.Surname, sm.TargetUser.Name, sm.TargetUser.Midname);
    }

    private static string BuildUserFullName(string surname, string name, string? midname)
    {
        var midnameSuffix = midname is not null ? " " + midname : string.Empty;
        return $"{surname} {name}{midnameSuffix}";
    }

    private static LastMessageInfo MapToLastMessageInfo(RawLastMessage msg) =>
        new(msg.Id, msg.Content, msg.CreatedAt, msg.IsSystemMessage,
            msg.SenderId, msg.IsVoiceMessage, msg.SystemEventType,
            msg.TargetUserId, msg.SenderName, msg.TargetUserName,
            msg.HasPoll, msg.HasFiles);

    private ChatDto BuildChatDto(ChatWithLastMessage item, Dictionary<int, int> unreadCounts, Dictionary<int, DialogPartnerInfo> dialogPartners)
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
        dto.ShowHistoryForNewMembers = item.Chat.ShowHistoryForNewMembers;

        return dto;
    }

    private void ApplyChatIdentity(ChatDto dto, ChatWithLastMessage item, Dictionary<int, DialogPartnerInfo> dialogPartners)
    {
        if (item.Chat.Type == ChatType.Contact && dialogPartners.TryGetValue(item.Chat.Id, out var partner))
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

        if (msg.HasFiles)
            return Truncate(msg.Content, 50);

        return Truncate(msg.Content, 50);
    }

    private static string? ExtractFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^1];
    }

    public async Task<Result<ChatDto>> GetChatForUserAsync(int chatId, int userId)
    {
        var access = await _accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure) return access.As<ChatDto>();

        var chat = await _context.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat is null)
            return Result<ChatDto>.NotFound($"Чат с ID {chatId} не найден");

        var dto = new ChatDto
        {
            Id = chat.Id,
            Type = chat.Type,
            CreatedById = chat.CreatedById ?? 0,
            LastMessageDate = chat.LastMessageTime,
            ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
        };

        if (chat.Type == ChatType.Contact)
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
            dto.Name = chat.Name;
            dto.Avatar = _urlBuilder.BuildUrl(chat.Avatar);
        }

        return Result<ChatDto>.Success(dto);
    }

    public async Task<Result<List<ChatDto>>> GetUserDialogsAsync(int userId)
    {
        var allChatsResult = await GetUserChatsAsync(userId);
        if (allChatsResult.IsFailure) return allChatsResult;

        var dialogs = allChatsResult.Value!
                                    .Where(c => c.Type == ChatType.Contact).ToList();
        return Result<List<ChatDto>>.Success(dialogs);
    }

    public async Task<Result<List<ChatDto>>> GetUserGroupsAsync(int userId)
    {
        var allChatsResult = await GetUserChatsAsync(userId);
        if (allChatsResult.IsFailure) return allChatsResult;

        var groups = allChatsResult.Value!
                                   .Where(c => c.Type != ChatType.Contact).ToList();
        return Result<List<ChatDto>>.Success(groups);
    }

    public async Task<Result<ChatDto>> GetContactChatAsync(int userId, int contactUserId)
    {
        var chat = await _context.Chats.Include(c => c.ChatMembers).Where(c => c.Type == ChatType.Contact)
            .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId)).Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId)).FirstOrDefaultAsync();

        if (chat is null)
            return Result<ChatDto>.NotFound("Диалог не найден");

        var dto = chat.ToDto(_urlBuilder);
        var partner = await GetDialogPartnerAsync(chat.Id, userId);

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

        var members = await _context.ChatMembers.Where(cm => cm.ChatId == chatId).Select(cm => new
        {
            cm.UserId,
            cm.Role,
            User = new
            {
                cm.User.Id,
                cm.User.Username,
                cm.User.Surname,
                cm.User.Name,
                cm.User.Midname,
                cm.User.Avatar,
                cm.User.LastOnline,
                cm.User.StatusType,
                cm.User.StatusExpiresAt
            }
        }).AsNoTracking().ToListAsync();

        var memberIds = members.ConvertAll(m => m.UserId);
        var onlineIds = _onlineService.FilterOnline(memberIds);

        var result = members.ConvertAll(m => new UserDto
        {
            Id = m.User.Id,
            Username = m.User.Username,
            DisplayName = FormatName(m.User.Surname, m.User.Name, m.User.Midname),
            Surname = m.User.Surname,
            Name = m.User.Name,
            Midname = m.User.Midname,
            Avatar = _urlBuilder.BuildUrl(m.User.Avatar),
            IsOnline = onlineIds.Contains(m.User.Id),
            LastOnline = m.User.LastOnline,
            StatusType = m.User.StatusType,
            StatusExpiresAt = m.User.StatusExpiresAt
        });

        return Result<List<UserDto>>.Success(result);
    }

    #endregion

    private static string FormatName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : "Без имени";
    }

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
            var chat = await PersistNewChatAsync(dto, contactUserId, ct);
            await transaction.CommitAsync(ct);

            InvalidateCachesAfterCreate(dto.CreatedById, contactUserId);

            if (chat.Type != ChatType.Contact)
                await _systemMessages.CreateAsync(chat.Id, dto.CreatedById, SystemEventType.ChatCreated);

            LogChatCreated(chat.Id, dto.CreatedById);

            return Result<ChatDto>.Success(new ChatDto
            {
                Id = chat.Id,
                Name = chat.Name,
                Type = chat.Type,
                CreatedById = dto.CreatedById
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

        var contactExists = await _context.Users.AnyAsync(u => u.Id == parsedId, ct);
        if (!contactExists)
            return Result<int?>.NotFound("Указанный собеседник не найден");

        var existing = await FindExistingContactChatAsync(dto.CreatedById, parsedId);
        if (existing is not null)
            return Result<int?>.Conflict("Диалог с этим пользователем уже существует");

        return Result<int?>.Success(parsedId);
    }

    private async Task<Data.Chat> PersistNewChatAsync(ChatDto dto, int? contactUserId, CancellationToken ct)
    {
        var chat = new Data.Chat
        {
            Name = dto.Type == ChatType.Contact ? null : dto.Name?.Trim(),
            Type = dto.Type,
            CreatedById = dto.CreatedById,
            CreatedAt = _appDateTime.UtcNow,
            ShowHistoryForNewMembers = dto.ShowHistoryForNewMembers
        };

        _context.Chats.Add(chat);
        await _context.SaveChangesAsync(ct);

        AddChatMembers(chat.Id, dto.CreatedById, contactUserId);
        await _context.SaveChangesAsync(ct);

        return chat;
    }

    private void AddChatMembers(int chatId, int creatorId, int? contactUserId)
    {
        _context.ChatMembers.Add(new ChatMember
        {
            ChatId = chatId,
            UserId = creatorId,
            Role = ChatRole.Owner,
            JoinedAt = _appDateTime.UtcNow
        });

        if (contactUserId.HasValue && contactUserId.Value != creatorId)
        {
            _context.ChatMembers.Add(new ChatMember
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

        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat is null)
            return Result<ChatDto>.NotFound($"Чат с ID {chatId} не найден");

        if (chat.Type == ChatType.Contact)
            return Result<ChatDto>.Failure("Нельзя редактировать диалог");

        await ApplyChatUpdatesAsync(chat, dto, userId, chatId);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<ChatDto>();

        LogChatUpdated(chatId, userId);

        var updatedDto = new ChatDto
        {
            Id = chat.Id,
            Name = chat.Name,
            Type = chat.Type,
            CreatedById = chat.CreatedById ?? 0,
            LastMessageDate = chat.LastMessageTime,
            Avatar = _urlBuilder.BuildUrl(chat.Avatar),
            ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
        };

        await _hubNotifier.SendToChatAsync(chatId, "ChatUpdated", updatedDto);
        return Result<ChatDto>.Success(updatedDto);
    }

    private async Task<Result> ApplyChatUpdatesAsync(Data.Chat chat, UpdateChatDto dto, int userId, int chatId)
    {
        if (!string.IsNullOrWhiteSpace(dto.Name))
            chat.Name = dto.Name.Trim();

        if (dto.ChatType.HasValue)
        {
            if (!await _accessControl.IsOwnerAsync(userId, chatId))
                return Result.Forbidden("Только владелец может изменить тип чата");

            chat.Type = dto.ChatType.Value;
        }

        if (dto.ShowHistoryForNewMembers.HasValue)
            chat.ShowHistoryForNewMembers = dto.ShowHistoryForNewMembers.Value;

        return Result.Success();
    }

    public async Task<Result> DeleteChatAsync(int chatId, int userId)
    {
        var owner = await _accessControl.EnsureOwnerOfAsync(userId, chatId);
        if (owner.IsFailure) return owner;

        var chat = await _context.Chats
                                 .Include(c => c.ChatMembers)
                                 .FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat is null)
            return Result.NotFound($"Чат с ID {chatId} не найден");

        var memberIds = chat.ChatMembers.Select(cm => cm.UserId).ToList();

        await DeleteVoiceFilesAsync(chatId);

        _context.Chats.Remove(chat);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save;

        foreach (var memberId in memberIds)
        {
            _cacheService.InvalidateUserChats(memberId);
            _cacheService.InvalidateMembership(memberId, chatId);
        }

        LogChatDeleted(chatId, userId);
        return Result.Success();
    }

    private async Task DeleteVoiceFilesAsync(int chatId)
    {
        var voiceFilePaths = await _context.VoiceMessages
            .Where(v => _context.UserMessages.Any(m => m.Id == v.MessageId && m.ChatId == chatId))
            .Select(v => v.FilePath)
            .ToListAsync();

        foreach (var path in voiceFilePaths)
            _fileService.DeleteFile(path);
    }

    public async Task<Result> RemoveChatAvatarAsync(int chatId, int userId)
    {
        var admin = await _accessControl.EnsureAdminOfAsync(userId, chatId);
        if (admin.IsFailure) return admin;

        var chatResult = await FindEntityAsync<Data.Chat>(chatId);
        if (chatResult.IsFailure) return chatResult;

        var chat = chatResult.Value!;

        if (chat.Type == ChatType.Contact)
            return Result.Failure("Нельзя удалить аватар у диалога");

        if (!string.IsNullOrEmpty(chat.Avatar))
            _fileService.DeleteFile(chat.Avatar);

        chat.Avatar = null;

        var dbSave = await SaveChangesAsync();
        if (dbSave.IsFailure) return dbSave;

        LogAvatarRemoved(chatId);
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

        var chatResult = await FindEntityAsync<Data.Chat>(chatId);
        if (chatResult.IsFailure) return chatResult.As<string>();

        var chat = chatResult.Value!;

        if (chat.Type == ChatType.Contact)
            return Result<string>.Failure("Нельзя установить аватар для диалога");

        var saveResult = await _fileService.SaveImageAsync(file, "chats", chat.Avatar);
        if (saveResult.IsFailure) return saveResult.As<string>();

        chat.Avatar = saveResult.Value;

        var dbSave = await SaveChangesAsync();
        if (dbSave.IsFailure) return dbSave.As<string>();

        LogAvatarUploaded(chatId);
        return Result<string>.Success(_urlBuilder.BuildUrl(saveResult.Value)!);
    }

    #endregion

    #region Private Helpers

    private async Task<Data.Chat?> FindExistingContactChatAsync(int userId, int contactUserId)
        => await _context.Chats.Include(c => c.ChatMembers).Where(c => c.Type == ChatType.Contact)
            .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId)).Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId)).FirstOrDefaultAsync();

    private async Task<DialogPartnerInfo?> GetDialogPartnerAsync(int chatId, int currentUserId)
        => (await GetDialogPartnersAsync([chatId], currentUserId)).GetValueOrDefault(chatId);

    private async Task<Dictionary<int, DialogPartnerInfo>> GetDialogPartnersAsync(List<int> chatIds, int currentUserId)
    {
        if (chatIds.Count == 0)
            return [];

        var partners = await _context.ChatMembers.Where(cm => chatIds.Contains(cm.ChatId) && cm.UserId != currentUserId).Select(cm => new
        {
            cm.ChatId,
            UserId = cm.User.Id,
            cm.User.Surname,
            cm.User.Name,
            cm.User.Midname,
            cm.User.Avatar,
            cm.User.StatusType,
            cm.User.StatusExpiresAt
        }).AsNoTracking().ToListAsync();

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

    private sealed class RawLastMessage
    {
        public int Id { get; init; }
        public int ChatId { get; init; }
        public DateTime CreatedAt { get; init; }
        public bool IsSystemMessage { get; init; }
        public int? SenderId { get; init; }
        public string? Content { get; init; }
        public SystemEventType? SystemEventType { get; init; }
        public int? TargetUserId { get; init; }
        public string? SenderName { get; init; }
        public string? TargetUserName { get; init; }
        public bool IsVoiceMessage { get; init; }
        public bool HasPoll { get; init; }
        public bool HasFiles { get; init; }
    }

    private sealed record LastMessageInfo(int Id, string? Content, DateTime CreatedAt, bool IsSystemMessage,
        int? SenderId, bool IsVoiceMessage, SystemEventType? SystemEventType, int? TargetUserId,
        string? SenderName, string? TargetUserName, bool HasPoll, bool HasFiles);

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
}