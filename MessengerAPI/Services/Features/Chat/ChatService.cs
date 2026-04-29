using MessengerAPI.Services.Base;
using MessengerAPI.Services.Features.Chat;
using MessengerAPI.Services.Infrastructure.Security;

namespace MessengerAPI.Services.Chat;

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
        return await _context.Chats.Where(c => chatIds.Contains(c.Id)).GroupJoin(_context.Messages.Where(m => m.IsDeleted != true), chat => chat.Id, msg => msg.ChatId,
            (chat, msgs) => new ChatWithLastMessage
            {
                Chat = chat,
                LastMessage = msgs.OrderByDescending(m => m.CreatedAt).Select(m => new LastMessageInfo(
                    m.Id, m.Content, m.CreatedAt, m.IsSystemMessage,
                    m.SenderId, m.IsVoiceMessage, m.SystemEventType, m.TargetUserId,
                    m.Sender!.FormatDisplayName(),
                    m.TargetUser != null ? m.TargetUser.FormatDisplayName() : null,
                    m.Polls.Count != 0,
                    m.MessageFiles.Count != 0)).FirstOrDefault()
            }).AsNoTracking().ToListAsync();
    }
    private ChatDto BuildChatDto(ChatWithLastMessage item, Dictionary<int, int> unreadCounts, Dictionary<int, DialogPartnerInfo> dialogPartners)
    {
        var msg = item.LastMessage;
        var (preview, senderName, isSystem) = msg is not null ? BuildLastMessagePreview(msg) : (null, null, false);

        var dto = new ChatDto
        {
            Id = item.Chat.Id,
            Type = item.Chat.Type,
            CreatedById = item.Chat.CreatedById ?? 0,
            LastMessageDate = msg?.CreatedAt ?? item.Chat.LastMessageTime,
            LastMessagePreview = preview,
            LastMessageSenderName = senderName,
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
    private static (string? Preview, string? SenderName, bool IsSystem) BuildLastMessagePreview(
        LastMessageInfo msg)
    {
        if (msg.IsSystemMessage)
            return (SystemMessageFormatter.Format(msg.SystemEventType, msg.SenderName, msg.TargetUserName), null, true);

        var preview = msg switch
        {
            { IsVoiceMessage: true } => "Голосовое сообщение",
            { HasFiles: true, Content: null or "" } => "Вложение",
            _ => Truncate(msg.Content, 50)
        };

        return (preview, msg.SenderName, false);
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

        var members = await _context.ChatMembers.Where(cm => cm.ChatId == chatId)
            .Include(cm => cm.User).AsNoTracking().ToListAsync();

        var memberIds = members.ConvertAll(m => m.UserId);
        var onlineIds = onlineService.FilterOnline(memberIds);

        var result = members.ConvertAll(m => new UserDto
        {
            Id = m.User.Id,
            Username = m.User.Username,
            DisplayName = m.User.FormatDisplayName(),
            Surname = m.User.Surname,
            Name = m.User.Name,
            Midname = m.User.Midname,
            Avatar = urlBuilder.BuildUrl(m.User.Avatar),
            IsOnline = onlineIds.Contains(m.User.Id),
            LastOnline = m.User.LastOnline
        });

        return Result<List<UserDto>>.Success(result);
    }

    #endregion

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

        await _context.Messages.Where(m => m.ChatId == chatId).ExecuteDeleteAsync();

        _context.ChatMembers.RemoveRange(chat.ChatMembers);
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
            .Include(cm => cm.User)
            .AsNoTracking()
            .ToListAsync();

        var partnerUserIds = partners.ConvertAll(p => p.User!.Id);
        var onlineIds = onlineService.FilterOnline(partnerUserIds);

        return partners.Where(p => p.User is not null).ToDictionary(p => p.ChatId, p => new DialogPartnerInfo
        {
            UserId = p.User!.Id,
            DisplayName = p.User.FormatDisplayName(),
            AvatarUrl = urlBuilder.BuildUrl(p.User.Avatar),
            StatusType = p.User.StatusType,
            StatusExpiresAt = p.User.StatusExpiresAt,
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
    }
}