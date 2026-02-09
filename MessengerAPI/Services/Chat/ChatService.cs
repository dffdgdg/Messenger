using MessengerAPI.Common;
using MessengerAPI.Configuration;
using MessengerAPI.Mapping;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerAPI.Services.Infrastructure;
using MessengerAPI.Services.Messaging;
using MessengerShared.DTO;
using MessengerShared.DTO.Chat;
using MessengerShared.DTO.User;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerAPI.Services.Chat
{
    public interface IChatService
    {
        Task<List<ChatDTO>> GetUserChatsAsync(int userId);
        Task<ChatDTO?> GetChatAsync(int chatId, int userId);
        Task<List<ChatDTO>> GetUserDialogsAsync(int userId);
        Task<List<ChatDTO>> GetUserGroupsAsync(int userId);
        Task<ChatDTO?> GetContactChatAsync(int userId, int contactUserId);

        Task<List<UserDTO>> GetChatMembersAsync(int chatId);

        Task<ChatDTO> CreateChatAsync(ChatDTO dto);
        Task<ChatDTO> UpdateChatAsync(int chatId, int userId, UpdateChatDTO dto);
        Task DeleteChatAsync(int chatId, int userId);
        Task<string> UploadChatAvatarAsync(int chatId, IFormFile file);

        Task EnsureUserHasChatAccessAsync(int userId, int chatId);
        Task EnsureUserIsChatAdminAsync(int userId, int chatId);
        Task EnsureUserIsChatOwnerAsync(int userId, int chatId);
    }

    public class ChatService(
    MessengerDbContext context,
    IAccessControlService accessControl,
    IFileService fileService,
    IOnlineUserService onlineService,
    IReadReceiptService readReceiptService,
    IUrlBuilder urlBuilder,
    ICacheService cacheService,
    IOptions<MessengerSettings> settings,
    ILogger<ChatService> logger)
    : BaseService<ChatService>(context, logger), IChatService
    {
        #region Access Control Delegation

        public Task EnsureUserHasChatAccessAsync(int userId, int chatId)
            => accessControl.EnsureIsMemberAsync(userId, chatId);

        public Task EnsureUserIsChatAdminAsync(int userId, int chatId)
            => accessControl.EnsureIsAdminAsync(userId, chatId);

        public Task EnsureUserIsChatOwnerAsync(int userId, int chatId)
            => accessControl.EnsureIsOwnerAsync(userId, chatId);

        #endregion

        #region Get Chats

        public async Task<List<ChatDTO>> GetUserChatsAsync(int userId)
        {
            var chatIds = await accessControl.GetUserChatIdsAsync(userId);
            if (chatIds.Count == 0) return [];

            var chatsData = await _context.Chats
                .Where(c => chatIds.Contains(c.Id))
                .GroupJoin(
                    _context.Messages.Where(m => m.IsDeleted != true),
                    chat => chat.Id,
                    msg => msg.ChatId,
                    (chat, msgs) => new
                    {
                        Chat = chat,
                        LastMessage = msgs
                            .OrderByDescending(m => m.CreatedAt)
                            .Select(m => new
                            {
                                m.Content,
                                m.CreatedAt,
                                SenderName = m.Sender!.FormatDisplayName()
                            })
                            .FirstOrDefault()
                    })
                .AsNoTracking()
                .ToListAsync();

            var unreadCounts = await readReceiptService
                .GetUnreadCountsForChatsAsync(userId, chatIds);

            var dialogChatIds = chatsData
                .Where(c => c.Chat.Type == ChatType.Contact)
                .Select(c => c.Chat.Id)
                .ToList();

            var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId);

            var result = chatsData.ConvertAll(item =>
            {
                var dto = new ChatDTO
                {
                    Id = item.Chat.Id,
                    Type = item.Chat.Type,
                    CreatedById = item.Chat.CreatedById ?? 0,
                    LastMessageDate = item.LastMessage?.CreatedAt ?? item.Chat.LastMessageTime,
                    LastMessagePreview = Truncate(item.LastMessage?.Content, 50),
                    LastMessageSenderName = item.LastMessage?.SenderName,
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

            return [.. result
            .OrderByDescending(c => c.UnreadCount > 0)
            .ThenByDescending(c => c.LastMessageDate)];
        }

        public async Task<ChatDTO?> GetChatAsync(int chatId, int userId)
        {
            var chat = await _context.Chats
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == chatId);

            if (chat is null)
            {
                _logger.LogWarning("Чат {ChatId} не найден", chatId);
                return null;
            }

            var dto = new ChatDTO
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

            return dto;
        }

        public async Task<List<ChatDTO>> GetUserDialogsAsync(int userId)
        {
            var allChats = await GetUserChatsAsync(userId);
            return [.. allChats.Where(c => c.Type == ChatType.Contact)];
        }

        public async Task<List<ChatDTO>> GetUserGroupsAsync(int userId)
        {
            var allChats = await GetUserChatsAsync(userId);
            return [.. allChats.Where(c => c.Type != ChatType.Contact)];
        }

        public async Task<ChatDTO?> GetContactChatAsync(int userId, int contactUserId)
        {
            var chat = await _context.Chats
                .Include(c => c.ChatMembers)
                .Where(c => c.Type == ChatType.Contact)
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId))
                .FirstOrDefaultAsync();

            if (chat is null)
                return null;

            var dto = chat.ToDto(urlBuilder);

            var partner = await GetDialogPartnerAsync(chat.Id, userId);
            if (partner is not null)
            {
                dto.Name = partner.Value.DisplayName;
                dto.Avatar = partner.Value.AvatarUrl;
            }

            return dto;
        }

        #endregion

        #region Members

        public async Task<List<UserDTO>> GetChatMembersAsync(int chatId)
        {
            var members = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId)
                .Include(cm => cm.User)
                .AsNoTracking()
                .ToListAsync();

            var memberIds = members.ConvertAll(m => m.UserId);
            var onlineIds = onlineService.FilterOnline(memberIds);

            return [.. members.Select(m => new UserDTO
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
        })];
        }

        #endregion

        #region Create / Update / Delete

        public async Task<ChatDTO> CreateChatAsync(ChatDTO dto)
        {
            if (dto.CreatedById <= 0)
                throw new ArgumentException("Некорректный ID создателя");

            int? contactUserId = null;
            if (dto.Type == ChatType.Contact
                && int.TryParse(dto.Name?.Trim(), out var parsedContactId))
            {
                contactUserId = parsedContactId;

                var contactExists = await _context.Users
                    .AnyAsync(u => u.Id == contactUserId);
                if (!contactExists)
                    throw new ArgumentException("Указанный собеседник не найден");

                var existingChat = await FindExistingContactChatAsync(
                    dto.CreatedById, contactUserId.Value);
                if (existingChat is not null)
                    throw new InvalidOperationException(
                        "Диалог с этим пользователем уже существует");
            }
            else if (dto.Type != ChatType.Contact && string.IsNullOrWhiteSpace(dto.Name))
            {
                throw new ArgumentException("Название чата обязательно");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var chat = new Model.Chat
                {
                    Name = dto.Type == ChatType.Contact ? null : dto.Name?.Trim(),
                    Type = dto.Type,
                    CreatedById = dto.CreatedById,
                    CreatedAt = AppDateTime.UtcNow
                };

                _context.Chats.Add(chat);
                await _context.SaveChangesAsync();

                _context.ChatMembers.Add(new ChatMember
                {
                    ChatId = chat.Id,
                    UserId = dto.CreatedById,
                    Role = ChatRole.Owner,
                    JoinedAt = AppDateTime.UtcNow
                });

                if (dto.Type == ChatType.Contact
                    && contactUserId.HasValue
                    && contactUserId.Value != dto.CreatedById)
                {
                    _context.ChatMembers.Add(new ChatMember
                    {
                        ChatId = chat.Id,
                        UserId = contactUserId.Value,
                        Role = ChatRole.Member,
                        JoinedAt = AppDateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                cacheService.InvalidateUserChats(dto.CreatedById);
                if (contactUserId.HasValue)
                    cacheService.InvalidateUserChats(contactUserId.Value);

                _logger.LogInformation("Чат {ChatId} создан пользователем {UserId}",
                    chat.Id, dto.CreatedById);

                return new ChatDTO
                {
                    Id = chat.Id,
                    Name = chat.Name,
                    Type = chat.Type,
                    CreatedById = dto.CreatedById
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<ChatDTO> UpdateChatAsync(
            int chatId, int userId, UpdateChatDTO dto)
        {
            await accessControl.EnsureIsAdminAsync(userId, chatId);

            var chat = await _context.Chats
                .FirstOrDefaultAsync(c => c.Id == chatId)
                ?? throw new KeyNotFoundException($"Чат с ID {chatId} не найден");

            if (chat.Type == ChatType.Contact)
                throw new InvalidOperationException("Нельзя редактировать диалог");

            if (!string.IsNullOrWhiteSpace(dto.Name))
                chat.Name = dto.Name.Trim();

            if (dto.ChatType.HasValue)
            {
                if (!await accessControl.IsOwnerAsync(userId, chatId))
                    throw new UnauthorizedAccessException(
                        "Только владелец может изменить тип чата");

                chat.Type = dto.ChatType.Value;
            }

            await SaveChangesAsync();

            _logger.LogInformation("Чат {ChatId} обновлён пользователем {UserId}",
                chatId, userId);

            return new ChatDTO
            {
                Id = chat.Id,
                Name = chat.Name,
                Type = chat.Type,
                CreatedById = chat.CreatedById ?? 0,
                LastMessageDate = chat.LastMessageTime,
                Avatar = urlBuilder.BuildUrl(chat.Avatar)
            };
        }

        public async Task DeleteChatAsync(int chatId, int userId)
        {
            await accessControl.EnsureIsOwnerAsync(userId, chatId);

            var chat = await _context.Chats
                .Include(c => c.ChatMembers)
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == chatId)
                ?? throw new KeyNotFoundException($"Чат с ID {chatId} не найден");

            var memberIds = chat.ChatMembers.Select(cm => cm.UserId).ToList();

            _context.ChatMembers.RemoveRange(chat.ChatMembers);
            _context.Messages.RemoveRange(chat.Messages);
            _context.Chats.Remove(chat);

            await SaveChangesAsync();

            foreach (var memberId in memberIds)
            {
                cacheService.InvalidateUserChats(memberId);
                cacheService.InvalidateMembership(memberId, chatId);
            }

            _logger.LogInformation("Чат {ChatId} удалён пользователем {UserId}",
                chatId, userId);
        }

        public async Task<string> UploadChatAvatarAsync(int chatId, IFormFile file)
        {
            if (file is null || file.Length == 0)
                throw new ArgumentException("Файл не загружен");

            if (!file.ContentType.StartsWith("image/"))
                throw new ArgumentException("Файл должен быть изображением");

            var chat = await GetRequiredEntityAsync<Model.Chat>(chatId);

            if (chat.Type == ChatType.Contact)
                throw new InvalidOperationException(
                    "Нельзя установить аватар для диалога");

            var avatarPath = await fileService.SaveImageAsync(
                file, "chats", chat.Avatar);
            chat.Avatar = avatarPath;

            await SaveChangesAsync();

            _logger.LogInformation("Аватар загружен для чата {ChatId}", chatId);

            return urlBuilder.BuildUrl(avatarPath)!;
        }

        #endregion

        #region Private Helpers

        private async Task<Model.Chat?> FindExistingContactChatAsync(
            int userId, int contactUserId)
        {
            return await _context.Chats
                .Include(c => c.ChatMembers)
                .Where(c => c.Type == ChatType.Contact)
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId))
                .FirstOrDefaultAsync();
        }

        private async Task<DialogPartnerInfo?> GetDialogPartnerAsync(
            int chatId, int currentUserId)
        {
            var partners = await GetDialogPartnersAsync([chatId], currentUserId);
            return partners.GetValueOrDefault(chatId);
        }

        private async Task<Dictionary<int, DialogPartnerInfo>> GetDialogPartnersAsync(
            List<int> chatIds, int currentUserId)
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
}