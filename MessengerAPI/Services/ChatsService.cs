using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IChatService
    {
        Task<List<ChatDTO>> GetUserChatsAsync(int userId, HttpRequest request);
        Task<ChatDTO?> GetChatAsync(int chatId, HttpRequest request);
        Task<List<UserDTO>> GetChatMembersAsync(int chatId, HttpRequest request);
        Task<ChatDTO> CreateChatAsync(ChatDTO chatDto);
        Task<ChatDTO> UpdateChatAsync(int chatId, int userId, UpdateChatDTO updateDto, HttpRequest request);
        Task DeleteChatAsync(int chatId, int userId);
        Task<string> UploadChatAvatarAsync(int chatId, IFormFile file, HttpRequest request);
        Task EnsureUserHasChatAccessAsync(int userId, int chatId);
        Task EnsureUserIsChatAdminAsync(int userId, int chatId);
        Task EnsureUserIsChatOwnerAsync(int userId, int chatId);
    }

    public class ChatService(
    MessengerDbContext context,
    IAccessControlService accessControl,
    IFileService fileService,
    IOnlineUserService onlineUserService,  // Добавить
    ILogger<ChatService> logger)
    : BaseService<ChatService>(context, logger), IChatService
    {
        #region Access Control Delegation

        public Task EnsureUserHasChatAccessAsync(int userId, int chatId)
            => accessControl.EnsureUserHasChatAccessAsync(userId, chatId);

        public Task EnsureUserIsChatAdminAsync(int userId, int chatId)
            => accessControl.EnsureUserIsChatAdminAsync(userId, chatId);

        public Task EnsureUserIsChatOwnerAsync(int userId, int chatId)
            => accessControl.EnsureUserIsChatOwnerAsync(userId, chatId);

        #endregion

        public async Task<List<ChatDTO>> GetUserChatsAsync(int userId, HttpRequest request)
        {
            try
            {
                var chatIds = await _context.ChatMembers
                    .Where(cm => cm.UserId == userId)
                    .Select(cm => cm.ChatId)
                    .ToListAsync();

                var chats = await _context.Chats
                    .Where(c => chatIds.Contains(c.Id))
                    .Select(c => new ChatDTO
                    {
                        Id = c.Id,
                        Name = c.Name,
                        IsGroup = c.IsGroup,
                        CreatedById = c.CreatedById ?? 0,
                        LastMessageDate = c.LastMessageTime,
                        Avatar = c.Avatar != null
                            ? $"{request.Scheme}://{request.Host}{c.Avatar}"
                            : null
                    })
                    .AsNoTracking()
                    .ToListAsync();

                _logger.LogDebug("Получено {ChatCount} чатов для пользователя {UserId}", chats.Count, userId);
                return chats;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "получение чатов пользователя", userId);
                throw;
            }
        }

        public async Task<ChatDTO?> GetChatAsync(int chatId, HttpRequest request)
        {
            try
            {
                var chat = await _context.Chats
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null)
                {
                    _logger.LogWarning("Чат {ChatId} не найден", chatId);
                    return null;
                }

                return new ChatDTO
                {
                    Id = chat.Id,
                    Name = chat.Name,
                    IsGroup = chat.IsGroup,
                    CreatedById = chat.CreatedById ?? 0,
                    LastMessageDate = chat.LastMessageTime,
                    Avatar = chat.Avatar != null
                        ? $"{request.Scheme}://{request.Host}{chat.Avatar}"
                        : null
                };
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "получение чата", chatId);
                throw;
            }
        }

        public async Task<List<UserDTO>> GetChatMembersAsync(int chatId, HttpRequest request)
        {
            try
            {
                var members = await _context.ChatMembers
                    .Where(cm => cm.ChatId == chatId)
                    .Include(cm => cm.User)
                    .Select(cm => new
                    {
                        cm.User.Id,
                        cm.User.Username,
                        cm.User.DisplayName,
                        cm.User.Avatar,
                        cm.User.LastOnline
                    })
                    .AsNoTracking()
                    .ToListAsync();

                var memberIds = members.Select(m => m.Id).ToList();
                var onlineIds = onlineUserService.FilterOnlineUserIds(memberIds);

                var result = members.Select(m => new UserDTO
                {
                    Id = m.Id,
                    Username = m.Username,
                    DisplayName = m.DisplayName,
                    Avatar = m.Avatar != null
                        ? $"{request.Scheme}://{request.Host}{m.Avatar}"
                        : null,
                    IsOnline = onlineIds.Contains(m.Id),
                    LastOnline = m.LastOnline
                }).ToList();

                _logger.LogDebug("Получено {MemberCount} участников чата {ChatId}", result.Count, chatId);
                return result;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "получение участников чата", chatId);
                throw;
            }
        }

        public async Task<ChatDTO> CreateChatAsync(ChatDTO chatDto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(chatDto.Name))
                    throw new ArgumentException("Название чата обязательно");

                if (chatDto.CreatedById <= 0)
                    throw new ArgumentException("Некорректный ID создателя");

                var chat = new Chat
                {
                    Name = chatDto.Name.Trim(),
                    IsGroup = chatDto.IsGroup,
                    CreatedById = chatDto.CreatedById,
                    CreatedAt = DateTime.Now
                };

                _context.Chats.Add(chat);
                await SaveChangesAsync();

                var member = new ChatMember
                {
                    ChatId = chat.Id,
                    UserId = chatDto.CreatedById,
                    Role = ChatRole.owner,
                    JoinedAt = DateTime.Now
                };

                _context.ChatMembers.Add(member);
                await SaveChangesAsync();

                chatDto.Id = chat.Id;

                _logger.LogInformation("Чат {ChatId} создан пользователем {UserId}", chat.Id, chatDto.CreatedById);
                return chatDto;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "создание чата");
                throw;
            }
        }

        /// <summary>
        /// Редактирование чата (только админ или владелец)
        /// </summary>
        public async Task<ChatDTO> UpdateChatAsync(int chatId, int userId, UpdateChatDTO updateDto, HttpRequest request)
        {
            try
            {
                // Проверка прав доступа
                await accessControl.EnsureUserIsChatAdminAsync(userId, chatId);

                var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId)
                    ?? throw new KeyNotFoundException($"Чат с ID {chatId} не найден");

                // Обновление названия
                if (!string.IsNullOrWhiteSpace(updateDto.Name))
                {
                    chat.Name = updateDto.Name.Trim();
                }

                // Обновление типа (группа/личный) - только для владельца
                if (updateDto.IsGroup.HasValue)
                {
                    var isOwner = await accessControl.IsChatOwnerAsync(userId, chatId);
                    if (!isOwner)
                        throw new UnauthorizedAccessException("Только владелец может изменить тип чата");

                    chat.IsGroup = updateDto.IsGroup.Value;
                }

                await SaveChangesAsync();

                _logger.LogInformation("Чат {ChatId} обновлён пользователем {UserId}", chatId, userId);

                return new ChatDTO
                {
                    Id = chat.Id,
                    Name = chat.Name,
                    IsGroup = chat.IsGroup,
                    CreatedById = chat.CreatedById ?? 0,
                    LastMessageDate = chat.LastMessageTime,
                    Avatar = chat.Avatar != null
                        ? $"{request.Scheme}://{request.Host}{chat.Avatar}"
                        : null
                };
            }
            catch (Exception ex) when (ex is not KeyNotFoundException
                                       && ex is not UnauthorizedAccessException)
            {
                LogOperationError(ex, "обновление чата", chatId);
                throw;
            }
        }

        /// <summary>
        /// Удаление чата (только владелец)
        /// </summary>
        public async Task DeleteChatAsync(int chatId, int userId)
        {
            try
            {
                await accessControl.EnsureUserIsChatOwnerAsync(userId, chatId);

                var chat = await _context.Chats.Include(c => c.ChatMembers).Include(c => c.Messages).FirstOrDefaultAsync(c => c.Id == chatId)
                    ?? throw new KeyNotFoundException($"Чат с ID {chatId} не найден");

                _context.ChatMembers.RemoveRange(chat.ChatMembers);
                _context.Messages.RemoveRange(chat.Messages);
                _context.Chats.Remove(chat);

                await SaveChangesAsync();

                _logger.LogInformation("Чат {ChatId} удалён пользователем {UserId}", chatId, userId);
            }
            catch (Exception ex) when (ex is not KeyNotFoundException
                                       && ex is not UnauthorizedAccessException)
            {
                LogOperationError(ex, "удаление чата", chatId);
                throw;
            }
        }

        public async Task<string> UploadChatAvatarAsync(int chatId, IFormFile file, HttpRequest request)
        {
            try
            {
                if (file == null || file.Length == 0)
                    throw new ArgumentException("Файл не загружен");

                if (!file.ContentType.StartsWith("image/"))
                    throw new ArgumentException("Файл должен быть изображением");

                var chat = await FindEntityAsync<Chat>(chatId);
                ValidateEntityExists(chat, "Чат", chatId);

                var avatarPath = await fileService.SaveImageAsync(file, "chats", chat.Avatar);
                chat.Avatar = avatarPath;

                await SaveChangesAsync();

                var fullAvatarUrl = $"{request.Scheme}://{request.Host}{avatarPath}";

                _logger.LogInformation("Аватар загружен для чата {ChatId}", chatId);
                return fullAvatarUrl;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "загрузка аватара чата", chatId);
                throw;
            }
        }
    }
}