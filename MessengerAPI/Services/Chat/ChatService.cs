using MessengerAPI.Services.Base;
using MessengerAPI.Services.Messaging;
using MessengerAPI.Services.ReadReceipt;

namespace MessengerAPI.Services.Chat;

public interface IChatService
{
    Task<Result<List<ChatDto>>> GetUserChatsAsync(int userId);
    Task<Result<ChatDto>> GetChatForUserAsync(int chatId, int userId);
    Task<Result<List<ChatDto>>> GetUserDialogsAsync(int userId);
    Task<Result<List<ChatDto>>> GetUserGroupsAsync(int userId);
    Task<Result<ChatDto>> GetContactChatAsync(int userId, int contactUserId);
    Task<Result<List<UserDto>>> GetChatMembersAsync(int chatId, int userId);
    Task<Result<ChatDto>> CreateChatAsync(ChatDto dto);
    Task<Result<ChatDto>> UpdateChatAsync(int chatId, int userId, UpdateChatDto dto);
    Task<Result> DeleteChatAsync(int chatId, int userId);
    Task<Result<string>> UploadChatAvatarAsync(int chatId, int userId, IFormFile file);
}

public class ChatService(
    MessengerDbContext context,
    IAccessControlService accessControl,
    IFileService fileService,
    IOnlineUserService onlineService,
    IReadReceiptService readReceiptService,
    IUrlBuilder urlBuilder,
    ICacheService cacheService,
    IHubNotifier hubNotifier,
    ISystemMessageService systemMessages,
    AppDateTime appDateTime,
    ILogger<ChatService> logger)
    : BaseService<ChatService>(context, logger), IChatService
{
    #region Get Chats

    public async Task<Result<List<ChatDto>>> GetUserChatsAsync(int userId)
    {
        var chatIds = await accessControl.GetUserChatIdsAsync(userId);
        if (chatIds.Count == 0)
            return Result<List<ChatDto>>.Success([]);

        var chatsData = await _context.Chats.Where(c => chatIds.Contains(c.Id))
            .GroupJoin(_context.Messages.Where(m => m.IsDeleted != true),
                chat => chat.Id, msg => msg.ChatId, (chat, msgs) => new
                {
                    Chat = chat,
                    LastMessage = msgs
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(m => new
                        {
                            m.Content,
                            m.CreatedAt,
                            m.IsSystemMessage,
                            SenderName = m.Sender!.FormatDisplayName()
                        }).FirstOrDefault()
                }).AsNoTracking().ToListAsync();

        var unreadCounts = await readReceiptService.GetUnreadCountsForChatsAsync(userId, chatIds);

        var dialogChatIds = chatsData.Where(c => c.Chat.Type == ChatType.Contact)
            .Select(c => c.Chat.Id).ToList();

        var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId);

        var result = chatsData.ConvertAll(item =>
        {
            var dto = new ChatDto
            {
                Id = item.Chat.Id,
                Type = item.Chat.Type,
                CreatedById = item.Chat.CreatedById ?? 0,
                LastMessageDate = item.LastMessage?.CreatedAt ?? item.Chat.LastMessageTime,
                LastMessagePreview = Truncate(item.LastMessage?.Content, 50),
                LastMessageSenderName = item.LastMessage?.IsSystemMessage == true
                    ? null
                    : item.LastMessage?.SenderName,
                UnreadCount = unreadCounts.GetValueOrDefault(item.Chat.Id, 0)
            };

            if (item.Chat.Type == ChatType.Contact
                && dialogPartners.TryGetValue(item.Chat.Id, out var partner))
            {
                dto.Name = partner.DisplayName;
                dto.Avatar = partner.AvatarUrl;
            }
            else
            {
                dto.Name = item.Chat.Name;
                dto.Avatar = urlBuilder.BuildUrl(item.Chat.Avatar);
            }

            return dto;
        });

        var sorted = result.OrderByDescending(c => c.UnreadCount > 0)
            .ThenByDescending(c => c.LastMessageDate).ToList();

        return Result<List<ChatDto>>.Success(sorted);
    }

    public async Task<Result<ChatDto>> GetChatForUserAsync(int chatId, int userId)
    {
        var accessResult = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (accessResult.IsFailure)
            return Result<ChatDto>.FromFailure(accessResult);

        var chat = await _context.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat is null)
            return Result<ChatDto>.NotFound($"Чат с ID {chatId} не найден");

        var dto = new ChatDto
        {
            Id = chat.Id,
            Type = chat.Type,
            CreatedById = chat.CreatedById ?? 0,
            LastMessageDate = chat.LastMessageTime
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
        var chat = await _context.Chats
            .Include(c => c.ChatMembers)
            .Where(c => c.Type == ChatType.Contact)
            .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
            .Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId))
            .FirstOrDefaultAsync();

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
        var accessResult = await accessControl.CheckIsMemberAsync(userId, chatId);
        if (accessResult.IsFailure)
            return Result<List<UserDto>>.FromFailure(accessResult);

        var members = await _context.ChatMembers
            .Where(cm => cm.ChatId == chatId)
            .Include(cm => cm.User)
            .AsNoTracking()
            .ToListAsync();

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

    #region Create / Update / Delete

    public async Task<Result<ChatDto>> CreateChatAsync(ChatDto dto)
    {
        if (dto.CreatedById <= 0)
            return Result<ChatDto>.Failure("Некорректный ID создателя");

        int? contactUserId = null;
        if (dto.Type == ChatType.Contact && int.TryParse(dto.Name?.Trim(), out var parsedContactId))
        {
            contactUserId = parsedContactId;

            var contactExists = await _context.Users.AnyAsync(u => u.Id == contactUserId);
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

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var chat = new Model.Chat
        {
            Name = dto.Type == ChatType.Contact ? null : dto.Name?.Trim(),
            Type = dto.Type,
            CreatedById = dto.CreatedById,
            CreatedAt = appDateTime.UtcNow
        };

        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

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

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        cacheService.InvalidateUserChats(dto.CreatedById);
        if (contactUserId.HasValue)
            cacheService.InvalidateUserChats(contactUserId.Value);

        if (chat.Type != ChatType.Contact)
        {
            var creator = await _context.Users.FindAsync(dto.CreatedById);
            await systemMessages.CreateAsync(chat.Id, dto.CreatedById,
                $"{creator?.FormatDisplayName() ?? "Пользователь"} создал группу",
                SystemEventType.ChatCreated);
        }

        _logger.LogInformation("Чат {ChatId} создан пользователем {UserId}",
            chat.Id, dto.CreatedById);

        return Result<ChatDto>.Success(new ChatDto
        {
            Id = chat.Id,
            Name = chat.Name,
            Type = chat.Type,
            CreatedById = dto.CreatedById
        });
    }

    public async Task<Result<ChatDto>> UpdateChatAsync(int chatId, int userId, UpdateChatDto dto)
    {
        var adminResult = await accessControl.CheckIsAdminAsync(userId, chatId);
        if (adminResult.IsFailure)
            return Result<ChatDto>.FromFailure(adminResult);

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

        var saveResult = await SaveChangesAsync();
        if (saveResult.IsFailure)
            return Result<ChatDto>.FromFailure(saveResult);

        _logger.LogInformation("Чат {ChatId} обновлён пользователем {UserId}", chatId, userId);

        return Result<ChatDto>.Success(new ChatDto
        {
            Id = chat.Id,
            Name = chat.Name,
            Type = chat.Type,
            CreatedById = chat.CreatedById ?? 0,
            LastMessageDate = chat.LastMessageTime,
            Avatar = urlBuilder.BuildUrl(chat.Avatar)
        });
    }

    public async Task<Result> DeleteChatAsync(int chatId, int userId)
    {
        var ownerResult = await accessControl.CheckIsOwnerAsync(userId, chatId);
        if (ownerResult.IsFailure)
            return ownerResult;

        var chat = await _context.Chats
            .Include(c => c.ChatMembers)
            .FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat is null)
            return Result.NotFound($"Чат с ID {chatId} не найден");

        var memberIds = chat.ChatMembers.Select(cm => cm.UserId).ToList();

        await _context.Messages.Where(m => m.ChatId == chatId).ExecuteDeleteAsync();

        _context.ChatMembers.RemoveRange(chat.ChatMembers);
        _context.Chats.Remove(chat);

        var saveResult = await SaveChangesAsync();
        if (saveResult.IsFailure)
            return saveResult;

        foreach (var memberId in memberIds)
        {
            cacheService.InvalidateUserChats(memberId);
            cacheService.InvalidateMembership(memberId, chatId);
        }

        _logger.LogInformation("Чат {ChatId} удалён пользователем {UserId}", chatId, userId);

        return Result.Success();
    }

    public async Task<Result<string>> UploadChatAvatarAsync(int chatId, int userId, IFormFile file)
    {
        var adminResult = await accessControl.CheckIsAdminAsync(userId, chatId);
        if (adminResult.IsFailure)
            return Result<string>.FromFailure(adminResult);

        if (file is null || file.Length == 0)
            return Result<string>.Failure("Файл не загружен");

        if (!file.ContentType.StartsWith("image/"))
            return Result<string>.Failure("Файл должен быть изображением");

        var chatResult = await FindEntityAsync<Model.Chat>(chatId);
        if (chatResult.IsFailure)
            return Result<string>.FromFailure(chatResult);

        var chat = chatResult.Value!;

        if (chat.Type == ChatType.Contact)
            return Result<string>.Failure("Нельзя установить аватар для диалога");

        var saveResult = await fileService.SaveImageAsync(file, "chats", chat.Avatar);
        if (saveResult.IsFailure)
            return Result<string>.FromFailure(saveResult);

        chat.Avatar = saveResult.Value;

        var dbSaveResult = await SaveChangesAsync();
        if (dbSaveResult.IsFailure)
            return Result<string>.FromFailure(dbSaveResult);

        _logger.LogInformation("Аватар загружен для чата {ChatId}", chatId);

        return Result<string>.Success(urlBuilder.BuildUrl(saveResult.Value)!);
    }

    #endregion

    #region Private Helpers

    private async Task<Model.Chat?> FindExistingContactChatAsync(int userId, int contactUserId)
    {
        return await _context.Chats
            .Include(c => c.ChatMembers)
            .Where(c => c.Type == ChatType.Contact)
            .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
            .Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId))
            .FirstOrDefaultAsync();
    }

    private async Task<DialogPartnerInfo?> GetDialogPartnerAsync(int chatId, int currentUserId)
    {
        var partners = await GetDialogPartnersAsync([chatId], currentUserId);
        return partners.GetValueOrDefault(chatId);
    }

    private async Task<Dictionary<int, DialogPartnerInfo>> GetDialogPartnersAsync(List<int> chatIds, int currentUserId)
    {
        if (chatIds.Count == 0)
            return [];

        var partners = await _context.ChatMembers
            .Where(cm => chatIds.Contains(cm.ChatId) && cm.UserId != currentUserId)
            .Include(cm => cm.User)
            .AsNoTracking()
            .ToListAsync();

        return partners
            .Where(p => p.User is not null)
            .ToDictionary(
                p => p.ChatId,
                p => new DialogPartnerInfo
                {
                    UserId = p.User!.Id,
                    DisplayName = p.User.FormatDisplayName(),
                    AvatarUrl = urlBuilder.BuildUrl(p.User.Avatar)
                });
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        return text.Length <= maxLength
            ? text
            : text[..maxLength] + "...";
    }

    #endregion

    private readonly record struct DialogPartnerInfo
    {
        public int UserId { get; init; }
        public string DisplayName { get; init; }
        public string? AvatarUrl { get; init; }
    }
}