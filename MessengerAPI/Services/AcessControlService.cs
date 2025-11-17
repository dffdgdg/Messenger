using MessengerAPI.Model;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IAccessControlService
    {
        Task<bool> IsUserChatMemberAsync(int userId, int chatId);
        Task<bool> IsChatOwnerAsync(int userId, int chatId);
        Task<bool> IsChatAdminAsync(int userId, int chatId);
        Task EnsureUserHasChatAccessAsync(int userId, int chatId);
        Task EnsureUserIsChatOwnerAsync(int userId, int chatId);
        Task EnsureUserIsChatAdminAsync(int userId, int chatId);
    }

    public class AccessControlService(MessengerDbContext context,ILogger<AccessControlService> logger) 
        : BaseService<AccessControlService>(context, logger), IAccessControlService
    {
        public async Task<bool> IsUserChatMemberAsync(int userId, int chatId)
        {
            try
            {
                var isMember = await _context.ChatMembers.AsNoTracking().AnyAsync(cm => cm.UserId == userId && cm.ChatId == chatId);

                _logger.LogDebug("User {UserId} membership in chat {ChatId}: {IsMember}", userId, chatId, isMember);

                return isMember;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "checking user chat membership", userId);
                throw;
            }
        }

        public async Task<bool> IsChatOwnerAsync(int userId, int chatId)
        {
            try
            {
                var isOwner = await _context.ChatMembers.AsNoTracking().AnyAsync(cm => cm.UserId == userId 
                && cm.ChatId == chatId && cm.Role == ChatRole.owner);

                _logger.LogDebug("User {UserId} owner status in chat {ChatId}: {IsOwner}",
                    userId, chatId, isOwner);

                return isOwner;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "checking user chat owner status", userId);
                throw;
            }
        }

        public async Task<bool> IsChatAdminAsync(int userId, int chatId)
        {
            try
            {
                var isAdmin = await _context.ChatMembers.AsNoTracking().AnyAsync
                    (cm => cm.UserId == userId && cm.ChatId == chatId && (cm.Role == ChatRole.admin || cm.Role == ChatRole.owner));

                _logger.LogDebug("User {UserId} admin status in chat {ChatId}: {IsAdmin}", userId, chatId, isAdmin);

                return isAdmin;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "checking user chat admin status", userId);
                throw;
            }
        }

        public async Task EnsureUserHasChatAccessAsync(int userId, int chatId)
        {
            try
            {
                var hasAccess = await IsUserChatMemberAsync(userId, chatId);

                if (!hasAccess)
                {
                    _logger.LogWarning("User {UserId} attempted to access chat {ChatId} without permission", userId, chatId);
                    throw new UnauthorizedAccessException($"User does not have access to chat {chatId}");
                }

                _logger.LogDebug("Access granted: User {UserId} to chat {ChatId}", userId, chatId);
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "ensuring user chat access", userId);
                throw new UnauthorizedAccessException("Error verifying chat access");
            }
        }

        public async Task EnsureUserIsChatOwnerAsync(int userId, int chatId)
        {
            try
            {
                var isOwner = await IsChatOwnerAsync(userId, chatId);

                if (!isOwner)
                {
                    _logger.LogWarning("User {UserId} attempted owner-only action in chat {ChatId} without permission", userId, chatId);
                    throw new UnauthorizedAccessException($"User is not the owner of chat {chatId}");
                }

                _logger.LogDebug("Owner access granted: User {UserId} to chat {ChatId}", userId, chatId);
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "ensuring user chat owner access", userId);
                throw new UnauthorizedAccessException("Error verifying chat owner access");
            }
        }

        public async Task EnsureUserIsChatAdminAsync(int userId, int chatId)
        {
            try
            {
                var isAdmin = await IsChatAdminAsync(userId, chatId);

                if (!isAdmin)
                {
                    _logger.LogWarning("User {UserId} attempted admin action in chat {ChatId} without permission", userId, chatId);
                    throw new UnauthorizedAccessException($"User does not have admin rights in chat {chatId}");
                }

                _logger.LogDebug("Admin access granted: User {UserId} to chat {ChatId}", userId, chatId);
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "ensuring user chat admin access", userId);
                throw new UnauthorizedAccessException("Error verifying chat admin access");
            }
        }

        public async Task<ChatRole?> GetUserRoleInChatAsync(int userId, int chatId)
        {
            try
            {
                var chatMember = await _context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ChatId == chatId);

                var role = chatMember?.Role;

                _logger.LogDebug("User {UserId} role in chat {ChatId}: {Role}", userId, chatId, role?.ToString() ?? "None");

                return role;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting user role in chat", userId);
                throw;
            }
        }
    }
}