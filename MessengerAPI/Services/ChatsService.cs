using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;

namespace MessengerAPI.Services
{
    public interface IChatService
    {
        Task<List<ChatDTO>> GetUserChatsAsync(int userId, HttpRequest request);
        Task<ChatDTO?> GetChatAsync(int chatId, HttpRequest request);
        Task<List<UserDTO>> GetChatMembersAsync(int chatId, HttpRequest request);
        Task<ChatDTO> CreateChatAsync(ChatDTO chatDto);
        Task<string> UploadChatAvatarAsync(int chatId, IFormFile file, HttpRequest request);
        Task EnsureUserHasChatAccessAsync(int userId, int chatId);
        Task EnsureUserIsChatAdminAsync(int userId, int chatId);
        Task EnsureUserIsChatOwnerAsync(int userId, int chatId);
    }

    public class ChatService(MessengerDbContext context, IWebHostEnvironment env, IFileService fileService, ILogger<ChatService> logger) 
        : BaseService<ChatService>(context, logger), IChatService
    {
        public async Task<List<ChatDTO>> GetUserChatsAsync(int userId, HttpRequest request)
        {
            try
            {
                var chatIds = await _context.ChatMembers.Where(cm => cm.UserId == userId).Select(cm => cm.ChatId).ToListAsync();

                var chats = await _context.Chats.Where(c => chatIds.Contains(c.Id)).Select(c => new ChatDTO
                    {
                        Id = c.Id,
                        Name = c.Name,
                        IsGroup = c.IsGroup,
                        CreatedById = c.CreatedById ?? 0,
                        LastMessageDate = c.LastMessageTime,
                        Avatar = c.Avatar != null ? $"{request.Scheme}://{request.Host}{c.Avatar}" : null
                    }).AsNoTracking().ToListAsync();

                _logger.LogDebug("Retrieved {ChatCount} chats for user {UserId}", chats.Count, userId);
                return chats;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting user chats", userId);
                throw;
            }
        }

        public async Task<ChatDTO?> GetChatAsync(int chatId, HttpRequest request)
        {
            try
            {
                var chat = await _context.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null)
                {
                    _logger.LogWarning("Chat {ChatId} not found", chatId);
                    return null;
                }

                var chatDto = new ChatDTO
                {
                    Id = chat.Id,
                    Name = chat.Name,
                    IsGroup = chat.IsGroup,
                    CreatedById = chat.CreatedById ?? 0,
                    LastMessageDate = chat.LastMessageTime,
                    Avatar = chat.Avatar != null ? $"{request.Scheme}://{request.Host}{chat.Avatar}" : null
                };

                return chatDto;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting chat", chatId);
                throw;
            }
        }

        public async Task EnsureUserHasChatAccessAsync(int userId, int chatId)
        {
            var hasAccess = await _context.ChatMembers.AnyAsync(cm => cm.UserId == userId && cm.ChatId == chatId);

            if (!hasAccess)
                throw new UnauthorizedAccessException($"User does not have access to chat {chatId}");
        }

        public async Task EnsureUserIsChatAdminAsync(int userId, int chatId)
        {
            var isAdmin = await _context.ChatMembers.AnyAsync(cm => cm.UserId == userId 
            && cm.ChatId == chatId && (cm.Role == ChatRole.admin || cm.Role == ChatRole.owner));

            if (!isAdmin)
                throw new UnauthorizedAccessException($"User does not have admin rights in chat {chatId}");
        }

        public async Task EnsureUserIsChatOwnerAsync(int userId, int chatId)
        {
            var isOwner = await _context.ChatMembers
                .AnyAsync(cm => cm.UserId == userId &&
                               cm.ChatId == chatId &&
                               cm.Role == ChatRole.owner);

            if (!isOwner)
                throw new UnauthorizedAccessException($"User is not the owner of chat {chatId}");
        }

        public async Task<List<UserDTO>> GetChatMembersAsync(int chatId, HttpRequest request)
        {
            try
            {
                var members = await _context.ChatMembers.Where(cm => cm.ChatId == chatId).Include(cm => cm.User).Select(cm => new UserDTO
                    {
                        Id = cm.User.Id,
                        Username = cm.User.Username,
                        DisplayName = cm.User.DisplayName,
                        Avatar = cm.User.Avatar != null ? $"{request.Scheme}://{request.Host}{cm.User.Avatar}" : null
                    }).AsNoTracking().ToListAsync();

                _logger.LogDebug("Retrieved {MemberCount} members for chat {ChatId}", members.Count, chatId);
                return members;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting chat members", chatId);
                throw;
            }
        }

        public async Task<ChatDTO> CreateChatAsync(ChatDTO chatDto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(chatDto.Name))
                    throw new ArgumentException("Chat name is required");

                if (chatDto.CreatedById <= 0)
                    throw new ArgumentException("Invalid creator user ID");

                var chat = new Chat
                {
                    Name = chatDto.Name.Trim(),
                    IsGroup = chatDto.IsGroup,
                    CreatedById = chatDto.CreatedById,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Chats.Add(chat);
                await SaveChangesAsync();

                var member = new ChatMember
                {
                    ChatId = chat.Id,
                    UserId = chatDto.CreatedById,
                    Role = ChatRole.owner,
                    JoinedAt = DateTime.UtcNow
                };

                _context.ChatMembers.Add(member);
                await SaveChangesAsync();

                chatDto.Id = chat.Id;

                _logger.LogInformation("Chat {ChatId} created by user {UserId}", chat.Id, chatDto.CreatedById);
                return chatDto;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error creating chat");
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "creating chat");
                throw;
            }
        }

        public async Task<string> UploadChatAvatarAsync(int chatId, IFormFile file, HttpRequest request)
        {
            try
            {
                if (file == null || file.Length == 0)
                    throw new ArgumentException("No file uploaded");

                if (!file.ContentType.StartsWith("image/"))
                    throw new ArgumentException("File must be an image");

                var chat = await FindEntityAsync<Chat>(chatId);
                ValidateEntityExists(chat, "Chat", chatId);

                var avatarPath = await fileService.SaveImageAsync(file, "chats", chat.Avatar);
                chat.Avatar = avatarPath;

                await SaveChangesAsync();

                var fullAvatarUrl = $"{request.Scheme}://{request.Host}{avatarPath}";

                _logger.LogInformation("Avatar uploaded for chat {ChatId}", chatId);
                return fullAvatarUrl;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid file for chat avatar {ChatId}", chatId);
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "uploading chat avatar", chatId);
                throw;
            }
        }
    }
}