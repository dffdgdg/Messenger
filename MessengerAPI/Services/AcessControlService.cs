using MessengerAPI.Model;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IAccessControlService
    {
        Task<bool> IsUserChatMemberAsync(int userId, int chatId);
        Task<bool> IsChatOwnerAsync(int userId, int chatId);
        Task<bool> IsChatAdminAsync(int userId, int chatId);
        Task<ChatRole?> GetUserRoleInChatAsync(int userId, int chatId);

        Task EnsureUserHasChatAccessAsync(int userId, int chatId);
        Task EnsureUserIsChatOwnerAsync(int userId, int chatId);
        Task EnsureUserIsChatAdminAsync(int userId, int chatId);
    }

    public class AccessControlService(MessengerDbContext context, ILogger<AccessControlService> logger) : IAccessControlService
    {
        public async Task<bool> IsUserChatMemberAsync(int userId, int chatId)
        {
            var isMember = await context.ChatMembers.AsNoTracking().AnyAsync(cm => cm.UserId == userId && cm.ChatId == chatId);

            logger.LogDebug("Членство пользователя {UserId} в чате {ChatId}: {IsMember}", userId, chatId, isMember);

            return isMember;
        }

        public async Task<bool> IsChatOwnerAsync(int userId, int chatId) => 
            await context.ChatMembers.AsNoTracking().AnyAsync(cm => cm.UserId == userId && cm.ChatId == chatId && cm.Role == ChatRole.owner);

        public async Task<bool> IsChatAdminAsync(int userId, int chatId) => 
            await context.ChatMembers.AsNoTracking().AnyAsync(cm => cm.UserId == userId && cm.ChatId == chatId && (cm.Role == ChatRole.admin || cm.Role == ChatRole.owner));

        public async Task<ChatRole?> GetUserRoleInChatAsync(int userId, int chatId)
        {
            var member = await context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ChatId == chatId);

            return member?.Role;
        }

        public async Task EnsureUserHasChatAccessAsync(int userId, int chatId)
        {
            if (!await IsUserChatMemberAsync(userId, chatId))
            {
                logger.LogWarning("Доступ запрещён: пользователь {UserId} к чату {ChatId}", userId, chatId);
                throw new UnauthorizedAccessException("У вас нет доступа к этому чату");
            }
        }

        public async Task EnsureUserIsChatOwnerAsync(int userId, int chatId)
        {
            if (!await IsChatOwnerAsync(userId, chatId))
            {
                logger.LogWarning("Доступ владельца запрещён: пользователь {UserId} к чату {ChatId}", userId, chatId);
                throw new UnauthorizedAccessException("Только владелец чата может выполнить это действие");
            }
        }

        public async Task EnsureUserIsChatAdminAsync(int userId, int chatId)
        {
            if (!await IsChatAdminAsync(userId, chatId))
            {
                logger.LogWarning("Доступ администратора запрещён: пользователь {UserId} к чату {ChatId}", userId, chatId);
                throw new UnauthorizedAccessException("Требуются права администратора");
            }
        }
    }
}