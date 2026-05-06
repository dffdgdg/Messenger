using API.Services.Base;
using API.Services.Features.Chat;
using API.Services.Infrastructure.Security;

namespace API.Services.Chat;

public partial class ChatService(MessengerDbContext context, IAccessControlService accessControl, IFileService fileService, IOnlineUserService onlineService,
    IReadReceiptService readReceiptService, IUrlBuilder urlBuilder, ICacheService cacheService, ISystemMessageService systemMessages, IHubNotifier hubNotifier,
    AppDateTime appDateTime, ILogger<ChatService> logger) : BaseService<ChatService>(context, logger), IChatService
{
    #region Get Chats

    private readonly IHubNotifier _hubNotifier = hubNotifier;

    public async Task<Result<List<ChatDto>>> GetUserChatsAsync(int userId)
    {
        var chatIds = await accessControl.GetUserChatIdsAsync(userId);
        if (chatIds.Count == 0)
            return Result<List<ChatDto>>.Success([]);

        var chatsData = await LoadChatsWithLastMessageAsync(chatIds);
        var unreadCounts = await readReceiptService.GetUnreadCountsForChatsAsync(userId, chatIds);
        var dialogChatIds = chatsData.Where(c => c.Chat.Type == ChatType.Contact).Select(c => c.Chat.Id).ToList();

        var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId);

        var result = chatsData.ConvertAll(item => BuildChatDto(item, unreadCounts, dialogPartners));

        return Result<List<ChatDto>>.Success([.. result.OrderByDescending(c => c.UnreadCount > 0).ThenByDescending(c => c.LastMessageDate)]);
    }
    private async Task<List<ChatWithLastMessage>> LoadChatsWithLastMessageAsync(List<int> chatIds)
    {
        var chats = await _context.Chats.Where(c => chatIds.Contains(c.Id)).AsNoTracking().ToListAsync();

        var lastMessageIds = await _context.Messages
            .Where(m => chatIds.Contains(m.ChatId) && m.IsDeleted != true)
            .GroupBy(m => m.ChatId)
            .Select(g => g.Max(m => m.Id))
            .ToListAsync();

        var lastMessages = lastMessageIds.Count > 0
            ? await _context.Messages
                .Where(m => lastMessageIds.Contains(m.Id))
                .Select(m => new
                {
                    m.Id,
                    m.ChatId,
                    m.CreatedAt,
                    IsSystemMessage = m is SystemMessage,
                    SenderId = m is UserMessage ? ((UserMessage)m).SenderId : ((SystemMessage)m).InitiatorId,
                    Content = m is UserMessage ? ((UserMessage)m).Content : ((SystemMessage)m).Content,
                    SystemEventType = m is SystemMessage ? (SystemEventType?)((SystemMessage)m).SystemEventType : null,
                    TargetUserId = m is SystemMessage ? ((SystemMessage)m).TargetUserId : null,
                    SenderName = m is UserMessage
                        ? ((UserMessage)m).Sender.Surname + " " + ((UserMessage)m).Sender.Name +
                          (((UserMessage)m).Sender.Midname != null ? " " + ((UserMessage)m).Sender.Midname : "")
                        : ((SystemMessage)m).Initiator != null
                            ? ((SystemMessage)m).Initiator!.Surname + " " + ((SystemMessage)m).Initiator!.Name +
                              (((SystemMessage)m).Initiator!.Midname != null ? " " + ((SystemMessage)m).Initiator!.Midname : "")
                            : null,
                    TargetUserName = m is SystemMessage && ((SystemMessage)m).TargetUser != null
                        ? ((SystemMessage)m).TargetUser!.Surname + " " + ((SystemMessage)m).TargetUser!.Name +
                          (((SystemMessage)m).TargetUser!.Midname != null ? " " + ((SystemMessage)m).TargetUser!.Midname : "")
                        : null,
                    IsVoiceMessage = m is UserMessage && ((UserMessage)m).VoiceMessage != null,
                    HasPoll = m is UserMessage && ((UserMessage)m).Poll != null,
                    HasFiles = m is UserMessage && ((UserMessage)m).MessageFiles.Any()
                })
                .ToListAsync()
            : [];

        var lastMsgMap = lastMessages.ToDictionary(m => m.ChatId);

        var result = new List<ChatWithLastMessage>(chats.Count);
        foreach (var chat in chats)
        {
            LastMessageInfo? lastMsg = null;
            if (lastMsgMap.TryGetValue(chat.Id, out var msg))
            {
                lastMsg = new LastMessageInfo(
                    msg.Id, msg.Content, msg.CreatedAt, msg.IsSystemMessage,
                    msg.SenderId, msg.IsVoiceMessage, msg.SystemEventType,
                    msg.TargetUserId, msg.SenderName, msg.TargetUserName,
                    msg.HasPoll, msg.HasFiles);
            }

            result.Add(new ChatWithLastMessage { Chat = chat, LastMessage = lastMsg });
        }

        return result;
    }
    private ChatDto BuildChatDto(ChatWithLastMessage item, Dictionary<int, int> unreadCounts, Dictionary<int, DialogPartnerInfo> dialogPartners)
    {
        var msg = item.LastMessage;
        var (preview, senderName, isSystem) = msg is not null ? BuildLastMessagePreview(msg) : (null, null, false);
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
            dto.Avatar = urlBuilder.BuildUrl(item.Chat.Avatar);
        }

        dto.ShowHistoryForNewMembers = item.Chat.ShowHistoryForNewMembers;

        return dto;
    }

    private static (string? Preview, string? SenderName, bool IsSystem) BuildLastMessagePreview(LastMessageInfo msg)
    {
        if (msg.IsSystemMessage)
            return (SystemMessageFormatter.Format(msg.SystemEventType, msg.SenderName, msg.TargetUserName), null, true);

        string? preview;

        if (msg.HasPoll)
        {
            preview = $"📊 {Truncate(msg.Content, 50) ?? "Опрос"}";
        }
        else if (msg.IsVoiceMessage)
        {
            preview = "Голосовое сообщение";
        }
        else if (msg.HasFiles && string.IsNullOrWhiteSpace(msg.Content))
        {
            preview = "Вложение";
        }
        else if (msg.HasFiles)
        {
            preview = $"{Truncate(msg.Content, 50)}";
        }
        else
        {
            preview = Truncate(msg.Content, 50);
        }

        string? firstName = null;
        if (!string.IsNullOrWhiteSpace(msg.SenderName))
        {
            var parts = msg.SenderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            firstName = parts[^1];
        }

        return (preview, firstName, false);
    }

    public async Task<Result<ChatDto>> GetChatForUserAsync(int chatId, int userId)
    {
        var access = await accessControl.EnsureMemberOfAsync(userId, chatId);
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
            dto.Avatar = urlBuilder.BuildUrl(chat.Avatar);
        }

        return Result<ChatDto>.Success(dto);
    }

    public async Task<Result<List<ChatDto>>> GetUserDialogsAsync(int userId)
    {
        var allChatsResult = await GetUserChatsAsync(userId);
        if (allChatsResult.IsFailure)
            return allChatsResult;

        var dialogs = allChatsResult.Value!.Where(c => c.Type == ChatType.Contact).ToList();
        return Result<List<ChatDto>>.Success(dialogs);
    }

    public async Task<Result<List<ChatDto>>> GetUserGroupsAsync(int userId)
    {
        var allChatsResult = await GetUserChatsAsync(userId);
        if (allChatsResult.IsFailure)
            return allChatsResult;

        var groups = allChatsResult.Value!.Where(c => c.Type != ChatType.Contact).ToList();
        return Result<List<ChatDto>>.Success(groups);
    }

    public async Task<Result<ChatDto>> GetContactChatAsync(int userId, int contactUserId)
    {
        var chat = await _context.Chats.Include(c => c.ChatMembers).Where(c => c.Type == ChatType.Contact).Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
            .Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId)).FirstOrDefaultAsync();

        if (chat is null)
            return Result<ChatDto>.NotFound("Диалог не найден");

        var dto = chat.ToDto(urlBuilder);

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
        var access = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure) return access.As<List<UserDto>>();

        var members = await _context.ChatMembers
            .Where(cm => cm.ChatId == chatId)
            .Select(cm => new
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
            })
            .AsNoTracking()
            .ToListAsync();

        var memberIds = members.ConvertAll(m => m.UserId);
        var onlineIds = onlineService.FilterOnline(memberIds);

        var result = members.ConvertAll(m => new UserDto
        {
            Id = m.User.Id,
            Username = m.User.Username,
            DisplayName = FormatName(m.User.Surname, m.User.Name, m.User.Midname),
            Surname = m.User.Surname,
            Name = m.User.Name,
            Midname = m.User.Midname,
            Avatar = urlBuilder.BuildUrl(m.User.Avatar),
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

        int? contactUserId = null;
        if (dto.Type == ChatType.Contact && int.TryParse(dto.Name?.Trim(), out var parsedContactId))
        {
            contactUserId = parsedContactId;

            var contactExists = await _context.Users.AnyAsync(u => u.Id == contactUserId, ct);
            if (!contactExists)
                return Result<ChatDto>.NotFound("Указанный собеседник не найден");

            var existingChat = await FindExistingContactChatAsync(dto.CreatedById, contactUserId.Value);
            if (existingChat is not null)
                return Result<ChatDto>.Conflict("Диалог с этим пользователем уже существует");
        }
        else if (dto.Type != ChatType.Contact && string.IsNullOrWhiteSpace(dto.Name))
        {
            return Result<ChatDto>.Failure("Название чата обязательно");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            var chat = new Data.Chat
            {
                Name = dto.Type == ChatType.Contact ? null : dto.Name?.Trim(),
                Type = dto.Type,
                CreatedById = dto.CreatedById,
                CreatedAt = appDateTime.UtcNow,
                ShowHistoryForNewMembers = dto.ShowHistoryForNewMembers
            };

            _context.Chats.Add(chat);
            await _context.SaveChangesAsync(ct);

            _context.ChatMembers.Add(new ChatMember
            {
                ChatId = chat.Id,
                UserId = dto.CreatedById,
                Role = ChatRole.Owner,
                JoinedAt = appDateTime.UtcNow
            });

            if (dto.Type == ChatType.Contact && contactUserId.HasValue && contactUserId.Value != dto.CreatedById)
            {
                _context.ChatMembers.Add(new ChatMember
                {
                    ChatId = chat.Id,
                    UserId = contactUserId.Value,
                    Role = ChatRole.Member,
                    JoinedAt = appDateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            cacheService.InvalidateUserChats(dto.CreatedById);
            if (contactUserId.HasValue)
                cacheService.InvalidateUserChats(contactUserId.Value);

            if (chat.Type != ChatType.Contact)
                await systemMessages.CreateAsync(chat.Id, dto.CreatedById, SystemEventType.ChatCreated);

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

    public async Task<Result<ChatDto>> UpdateChatAsync(int chatId, int userId, UpdateChatDto dto)
    {
        var admin = await accessControl.EnsureAdminOfAsync(userId, chatId);
        if (admin.IsFailure) return admin.As<ChatDto>();

        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat is null)
            return Result<ChatDto>.NotFound($"Чат с ID {chatId} не найден");

        if (chat.Type == ChatType.Contact)
            return Result<ChatDto>.Failure("Нельзя редактировать диалог");

        if (!string.IsNullOrWhiteSpace(dto.Name))
            chat.Name = dto.Name.Trim();

        if (dto.ChatType.HasValue)
        {
            if (!await accessControl.IsOwnerAsync(userId, chatId))
                return Result<ChatDto>.Forbidden("Только владелец может изменить тип чата");

            chat.Type = dto.ChatType.Value;
        }

        if (dto.ShowHistoryForNewMembers.HasValue)
        {
            chat.ShowHistoryForNewMembers = dto.ShowHistoryForNewMembers.Value;
        }

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
            Avatar = urlBuilder.BuildUrl(chat.Avatar),
            ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
        };

        await _hubNotifier.SendToChatAsync(chatId, "ChatUpdated", updatedDto);

        return Result<ChatDto>.Success(updatedDto);
    }

    public async Task<Result> DeleteChatAsync(int chatId, int userId)
    {
        var owner = await accessControl.EnsureOwnerOfAsync(userId, chatId);
        if (owner.IsFailure) return owner;

        var chat = await _context.Chats.Include(c => c.ChatMembers).FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat is null)
            return Result.NotFound($"Чат с ID {chatId} не найден");

        var memberIds = chat.ChatMembers.Select(cm => cm.UserId).ToList();

        var voiceFilePaths = await _context.VoiceMessages.Where(v => _context.UserMessages.Any(m => m.Id == v.MessageId && m.ChatId == chatId))
            .Select(v => v.FilePath).ToListAsync();

        foreach (var path in voiceFilePaths)
            fileService.DeleteFile(path);

        _context.Chats.Remove(chat);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save;

        foreach (var memberId in memberIds)
        {
            cacheService.InvalidateUserChats(memberId);
            cacheService.InvalidateMembership(memberId, chatId);
        }

        LogChatDeleted(chatId, userId);
        return Result.Success();
    }

    public async Task<Result> RemoveChatAvatarAsync(int chatId, int userId)
    {
        var admin = await accessControl.EnsureAdminOfAsync(userId, chatId);
        if (admin.IsFailure) return admin;

        var chatResult = await FindEntityAsync<Data.Chat>(chatId);
        if (chatResult.IsFailure) return chatResult;

        var chat = chatResult.Value!;

        if (chat.Type == ChatType.Contact)
            return Result.Failure("Нельзя удалить аватар у диалога");

        if (!string.IsNullOrEmpty(chat.Avatar))
            fileService.DeleteFile(chat.Avatar);

        chat.Avatar = null;

        var dbSave = await SaveChangesAsync();
        if (dbSave.IsFailure) return dbSave;

        LogAvatarRemoved(chatId);

        return Result.Success();
    }

    public async Task<Result<string>> UploadChatAvatarAsync(int chatId, int userId, IFormFile file)
    {
        var admin = await accessControl.EnsureAdminOfAsync(userId, chatId);
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

        var saveResult = await fileService.SaveImageAsync(file, "chats", chat.Avatar);
        if (saveResult.IsFailure) return saveResult.As<string>();

        chat.Avatar = saveResult.Value;

        var dbSave = await SaveChangesAsync();
        if (dbSave.IsFailure) return dbSave.As<string>();

        LogAvatarUploaded(chatId);

        return Result<string>.Success(urlBuilder.BuildUrl(saveResult.Value)!);
    }

    #endregion

    #region Private Helpers

    private async Task<Data.Chat?> FindExistingContactChatAsync(int userId, int contactUserId)
        => await _context.Chats.Include(c => c.ChatMembers).Where(c => c.Type == ChatType.Contact).Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
            .Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId)).FirstOrDefaultAsync();

    private async Task<DialogPartnerInfo?> GetDialogPartnerAsync(int chatId, int currentUserId)
        => (await GetDialogPartnersAsync([chatId], currentUserId)).GetValueOrDefault(chatId);

    private async Task<Dictionary<int, DialogPartnerInfo>> GetDialogPartnersAsync(List<int> chatIds, int currentUserId)
    {
        if (chatIds.Count == 0)
            return [];

        var partners = await _context.ChatMembers
            .Where(cm => chatIds.Contains(cm.ChatId) && cm.UserId != currentUserId)
            .Select(cm => new
            {
                cm.ChatId,
                UserId = cm.User.Id,
                cm.User.Surname,
                cm.User.Name,
                cm.User.Midname,
                cm.User.Avatar,
                cm.User.StatusType,
                cm.User.StatusExpiresAt
            })
            .AsNoTracking()
            .ToListAsync();

        var partnerUserIds = partners.ConvertAll(p => p.UserId);
        var onlineIds = onlineService.FilterOnline(partnerUserIds);

        return partners.ToDictionary(p => p.ChatId, p => new DialogPartnerInfo
        {
            UserId = p.UserId,
            DisplayName = FormatName(p.Surname, p.Name, p.Midname),
            AvatarUrl = urlBuilder.BuildUrl(p.Avatar),
            StatusType = p.StatusType,
            StatusExpiresAt = p.StatusExpiresAt,
            IsOnline = onlineIds.Contains(p.UserId)
        });
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return null;

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
    private sealed record LastMessageInfo(int Id, string? Content, DateTime CreatedAt, bool IsSystemMessage, int? SenderId, bool IsVoiceMessage,
        SystemEventType? SystemEventType, int? TargetUserId, string? SenderName, string? TargetUserName, bool HasPoll, bool HasFiles);

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