using MessengerAPI.Configuration;
using MessengerAPI.Helpers;
using MessengerAPI.Model;
using MessengerShared.DTO;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerAPI.Services
{
    public interface IChatService
    {
        // Получение чатов
        Task<List<ChatDTO>> GetUserChatsAsync(int userId, HttpRequest request);
        Task<ChatDTO?> GetChatAsync(int chatId, int userId, HttpRequest request);
        Task<List<ChatDTO>> GetUserDialogsAsync(int userId, HttpRequest request);
        Task<List<ChatDTO>> GetUserGroupsAsync(int userId, HttpRequest request);
        Task<ChatDTO?> GetContactChatAsync(int userId, int contactUserId, HttpRequest request);

        // Участники
        Task<List<UserDTO>> GetChatMembersAsync(int chatId, HttpRequest request);

        // CRUD
        Task<ChatDTO> CreateChatAsync(ChatDTO dto);
        Task<ChatDTO> UpdateChatAsync(int chatId, int userId, UpdateChatDTO dto, HttpRequest request);
        Task DeleteChatAsync(int chatId, int userId);
        Task<string> UploadChatAvatarAsync(int chatId, IFormFile file, HttpRequest request);

        // Access Control (делегирование)
        Task EnsureUserHasChatAccessAsync(int userId, int chatId);
        Task EnsureUserIsChatAdminAsync(int userId, int chatId);
        Task EnsureUserIsChatOwnerAsync(int userId, int chatId);
    }
    public class ChatService(MessengerDbContext context,IAccessControlService accessControl,IFileService fileService,
        IOnlineUserService onlineService,IReadReceiptService readReceiptService,IOptions<MessengerSettings> settings,
        ILogger<ChatService> logger) : BaseService<ChatService>(context, logger), IChatService
    {
        #region Access Control Delegation

        public Task EnsureUserHasChatAccessAsync(int userId, int chatId)=> accessControl.EnsureIsMemberAsync(userId, chatId);

        public Task EnsureUserIsChatAdminAsync(int userId, int chatId)=> accessControl.EnsureIsAdminAsync(userId, chatId);

        public Task EnsureUserIsChatOwnerAsync(int userId, int chatId)=> accessControl.EnsureIsOwnerAsync(userId, chatId);

        #endregion

        #region Get Chats

        public async Task<List<ChatDTO>> GetUserChatsAsync(int userId, HttpRequest request)
        {
            var chatIds = await _context.ChatMembers.Where(cm => cm.UserId == userId).Select(cm => cm.ChatId).ToListAsync();

            if (chatIds.Count == 0)
                return [];

            var chatsData = await _context.Chats.Where(c => chatIds.Contains(c.Id)).Select(c => new
            {
                Chat = c, LastMessage = c.Messages.Where(m => m.IsDeleted != true).OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Content,
                    m.CreatedAt,
                    SenderName = m.Sender != null ? m.Sender.Name ?? m.Sender.Username : null
                })
                .FirstOrDefault()
            }).AsNoTracking().ToListAsync();

            var unreadCounts = await readReceiptService.GetUnreadCountsForChatsAsync(userId, chatIds);

            // Получаем информацию о собеседниках для диалогов
            var dialogChatIds = chatsData.Where(c => c.Chat.Type == ChatType.Contact).Select(c => c.Chat.Id).ToList();

            var dialogPartners = await GetDialogPartnersAsync(dialogChatIds, userId, request);

            var result = new List<ChatDTO>();

            foreach (var item in chatsData)
            {
                var dto = new ChatDTO
                {
                    Id = item.Chat.Id,
                    Type = item.Chat.Type,
                    CreatedById = item.Chat.CreatedById ?? 0,
                    LastMessageDate = item.LastMessage?.CreatedAt ?? item.Chat.LastMessageTime,
                    LastMessagePreview = TruncateText(item.LastMessage?.Content, 50),
                    LastMessageSenderName = item.LastMessage?.SenderName,
                    UnreadCount = unreadCounts.GetValueOrDefault(item.Chat.Id, 0)
                };

                // Для диалогов подставляем данные собеседника
                if (item.Chat.Type == ChatType.Contact &&
                    dialogPartners.TryGetValue(item.Chat.Id, out var partner))
                {
                    dto.Name = partner.DisplayName;
                    dto.Avatar = partner.AvatarUrl;
                }
                else
                {
                    dto.Name = item.Chat.Name;
                    dto.Avatar = BuildAvatarUrl(item.Chat.Avatar, request);
                }

                result.Add(dto);
            }

            // Сортировка: сначала непрочитанные, затем по дате
            return [.. result.OrderByDescending(c => c.UnreadCount > 0).ThenByDescending(c => c.LastMessageDate)];
        }

        public async Task<ChatDTO?> GetChatAsync(int chatId, int userId, HttpRequest request)
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

            // Для диалогов получаем данные собеседника
            if (chat.Type == ChatType.Contact)
            {
                var partner = await GetDialogPartnerAsync(chatId, userId, request);
                if (partner is not null)
                {
                    dto.Name = partner.Value.DisplayName;
                    dto.Avatar = partner.Value.AvatarUrl;
                }
            }
            else
            {
                dto.Name = chat.Name;
                dto.Avatar = BuildAvatarUrl(chat.Avatar, request);
            }

            return dto;
        }

        public async Task<List<ChatDTO>> GetUserDialogsAsync(int userId, HttpRequest request)
        {
            var allChats = await GetUserChatsAsync(userId, request);
            return [.. allChats.Where(c => c.Type == ChatType.Contact)];
        }

        public async Task<List<ChatDTO>> GetUserGroupsAsync(int userId, HttpRequest request)
        {
            var allChats = await GetUserChatsAsync(userId, request);
            return [.. allChats.Where(c => c.Type != ChatType.Contact)];
        }

        public async Task<ChatDTO?> GetContactChatAsync(int userId, int contactUserId, HttpRequest request)
        {
            var chat = await _context.Chats.Include(c => c.ChatMembers).Where(c => c.Type == ChatType.Contact)
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId)).FirstOrDefaultAsync();

            if (chat is null)
                return null;

            var dto = chat.ToDto(request);

            // Подставляем имя собеседника
            var partner = await GetDialogPartnerAsync(chat.Id, userId, request);
            if (partner is not null)
            {
                dto.Name = partner.Value.DisplayName;
                dto.Avatar = partner.Value.AvatarUrl;
            }

            return dto;
        }

        #endregion

        #region Members

        public async Task<List<UserDTO>> GetChatMembersAsync(int chatId, HttpRequest request)
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
                Avatar = BuildAvatarUrl(m.User.Avatar, request),
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

            // Для диалогов: Name содержит ID собеседника (временно, для совместимости)
            int? contactUserId = null;
            if (dto.Type == ChatType.Contact && int.TryParse(dto.Name?.Trim(), out var parsedContactId))
            {
                contactUserId = parsedContactId;

                var contactExists = await _context.Users.AnyAsync(u => u.Id == contactUserId);
                if (!contactExists)
                    throw new ArgumentException("Указанный собеседник не найден");

                // Проверяем, нет ли уже диалога
                var existingChat = await FindExistingContactChatAsync(dto.CreatedById, contactUserId.Value);
                if (existingChat is not null)
                    throw new InvalidOperationException("Диалог с этим пользователем уже существует");
            }
            else if (dto.Type != ChatType.Contact && string.IsNullOrWhiteSpace(dto.Name))
            {
                throw new ArgumentException("Название чата обязательно");
            }

            var chat = new Chat
            {
                Name = dto.Type == ChatType.Contact ? null : dto.Name?.Trim(),
                Type = dto.Type,
                CreatedById = dto.CreatedById,
                CreatedAt = DateTime.UtcNow
            };

            _context.Chats.Add(chat);
            await SaveChangesAsync();

            // Добавляем создателя
            _context.ChatMembers.Add(new ChatMember
            {
                ChatId = chat.Id,
                UserId = dto.CreatedById,
                Role = ChatRole.Owner,
                JoinedAt = DateTime.UtcNow
            });

            // Для диалогов добавляем собеседника
            if (dto.Type == ChatType.Contact && contactUserId.HasValue && contactUserId.Value != dto.CreatedById)
            {
                _context.ChatMembers.Add(new ChatMember
                {
                    ChatId = chat.Id,
                    UserId = contactUserId.Value,
                    Role = ChatRole.Member,
                    JoinedAt = DateTime.UtcNow
                });
            }

            await SaveChangesAsync();

            _logger.LogInformation("Чат {ChatId} создан пользователем {UserId}", chat.Id, dto.CreatedById);

            return new ChatDTO
            {
                Id = chat.Id,
                Name = chat.Name,
                Type = chat.Type,
                CreatedById = dto.CreatedById
            };
        }

        public async Task<ChatDTO> UpdateChatAsync(int chatId, int userId, UpdateChatDTO dto, HttpRequest request)
        {
            await accessControl.EnsureIsAdminAsync(userId, chatId);

            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId)
                ?? throw new KeyNotFoundException($"Чат с ID {chatId} не найден");

            if (chat.Type == ChatType.Contact)
                throw new InvalidOperationException("Нельзя редактировать диалог");

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                chat.Name = dto.Name.Trim();
            }

            if (dto.ChatType.HasValue)
            {
                if (!await accessControl.IsOwnerAsync(userId, chatId))
                    throw new UnauthorizedAccessException("Только владелец может изменить тип чата");

                chat.Type = dto.ChatType.Value;
            }

            await SaveChangesAsync();

            _logger.LogInformation("Чат {ChatId} обновлён пользователем {UserId}", chatId, userId);

            return new ChatDTO
            {
                Id = chat.Id,
                Name = chat.Name,
                Type = chat.Type,
                CreatedById = chat.CreatedById ?? 0,
                LastMessageDate = chat.LastMessageTime,
                Avatar = BuildAvatarUrl(chat.Avatar, request)
            };
        }

        public async Task DeleteChatAsync(int chatId, int userId)
        {
            await accessControl.EnsureIsOwnerAsync(userId, chatId);

            var chat = await _context.Chats.Include(c => c.ChatMembers).Include(c => c.Messages).FirstOrDefaultAsync(c => c.Id == chatId)
                ?? throw new KeyNotFoundException($"Чат с ID {chatId} не найден");

            _context.ChatMembers.RemoveRange(chat.ChatMembers);
            _context.Messages.RemoveRange(chat.Messages);
            _context.Chats.Remove(chat);

            await SaveChangesAsync();

            _logger.LogInformation("Чат {ChatId} удалён пользователем {UserId}", chatId, userId);
        }

        public async Task<string> UploadChatAvatarAsync(int chatId, IFormFile file, HttpRequest request)
        {
            if (file is null || file.Length == 0)
                throw new ArgumentException("Файл не загружен");

            if (!file.ContentType.StartsWith("image/"))
                throw new ArgumentException("Файл должен быть изображением");

            var chat = await GetRequiredEntityAsync<Chat>(chatId);

            if (chat.Type == ChatType.Contact)
                throw new InvalidOperationException("Нельзя установить аватар для диалога");

            var avatarPath = await fileService.SaveImageAsync(file, "chats", chat.Avatar);
            chat.Avatar = avatarPath;

            await SaveChangesAsync();

            _logger.LogInformation("Аватар загружен для чата {ChatId}", chatId);

            return $"{request.Scheme}://{request.Host}{avatarPath}";
        }

        #endregion

        #region Private Helpers

        private async Task<Chat?> FindExistingContactChatAsync(int userId, int contactUserId)
        {
            return await _context.Chats.Include(c => c.ChatMembers).Where(c => c.Type == ChatType.Contact)
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == contactUserId)).FirstOrDefaultAsync();
        }

        private async Task<DialogPartnerInfo?> GetDialogPartnerAsync(int chatId, int currentUserId, HttpRequest request)
        {
            var partners = await GetDialogPartnersAsync([chatId], currentUserId, request);
            return partners.GetValueOrDefault(chatId);
        }

        private async Task<Dictionary<int, DialogPartnerInfo>> GetDialogPartnersAsync(List<int> chatIds,int currentUserId,HttpRequest request)
        {
            if (chatIds.Count == 0)
                return [];

            var partners = await _context.ChatMembers.Where(cm => chatIds.Contains(cm.ChatId) && cm.UserId != currentUserId)
                .Include(cm => cm.User).AsNoTracking().ToListAsync();

            return partners.Where(p => p.User is not null).ToDictionary(
                p => p.ChatId,
                p => new DialogPartnerInfo
                {
                    UserId = p.User!.Id,
                    DisplayName = p.User.FormatDisplayName(),
                    AvatarUrl = BuildAvatarUrl(p.User.Avatar, request)
                });
        }

        private static string? BuildAvatarUrl(string? avatarPath, HttpRequest request)
        {
            if (string.IsNullOrEmpty(avatarPath))
                return null;

            return $"{request.Scheme}://{request.Host}{avatarPath}";
        }

        private static string? TruncateText(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }

        #endregion

        private record struct DialogPartnerInfo
        {
            public int UserId { get; init; }
            public string DisplayName { get; init; }
            public string? AvatarUrl { get; init; }
        }
    }
}