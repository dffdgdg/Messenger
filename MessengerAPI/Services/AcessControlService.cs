using MessengerAPI.Model;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IAccessControlService
    {
        Task<bool> IsMemberAsync(int userId, int chatId);
        Task<bool> IsOwnerAsync(int userId, int chatId);
        Task<bool> IsAdminAsync(int userId, int chatId);
        Task<ChatRole?> GetRoleAsync(int userId, int chatId);

        Task EnsureIsMemberAsync(int userId, int chatId);
        Task EnsureIsOwnerAsync(int userId, int chatId);
        Task EnsureIsAdminAsync(int userId, int chatId);

        Task<List<int>> GetUserChatIdsAsync(int userId);
    }

    public class AccessControlService(MessengerDbContext context, ICacheService cache,ILogger<AccessControlService> logger) : IAccessControlService
    {
        private readonly Dictionary<(int UserId, int ChatId), ChatMember?> _requestCache = [];

        public async Task<List<int>> GetUserChatIdsAsync(int userId)
        {
            return await cache.GetUserChatIdsAsync(userId, async () =>
            {
                return await context.ChatMembers
                    .Where(cm => cm.UserId == userId)
                    .Select(cm => cm.ChatId)
                    .ToListAsync();
            });
        }

        public async Task<bool> IsMemberAsync(int userId, int chatId)
        {
            var member = await GetMembershipAsync(userId, chatId);
            return member is not null;
        }

        public async Task<bool> IsOwnerAsync(int userId, int chatId)
        {
            var member = await GetMembershipAsync(userId, chatId);
            return member?.Role == ChatRole.Owner;
        }

        public async Task<bool> IsAdminAsync(int userId, int chatId)
        {
            var member = await GetMembershipAsync(userId, chatId);
            return member?.Role is ChatRole.Admin or ChatRole.Owner;
        }

        public async Task<ChatRole?> GetRoleAsync(int userId, int chatId)
        {
            var member = await GetMembershipAsync(userId, chatId);
            return member?.Role;
        }

        public async Task EnsureIsMemberAsync(int userId, int chatId)
        {
            if (!await IsMemberAsync(userId, chatId))
            {
                logger.LogWarning("Доступ запрещён: пользователь {UserId} к чату {ChatId}", userId, chatId);
                throw new UnauthorizedAccessException("У вас нет доступа к этому чату");
            }
        }

        public async Task EnsureIsOwnerAsync(int userId, int chatId)
        {
            if (!await IsOwnerAsync(userId, chatId))
            {
                logger.LogWarning("Требуются права владельца: пользователь {UserId}, чат {ChatId}", userId, chatId);
                throw new UnauthorizedAccessException("Только владелец чата может выполнить это действие");
            }
        }

        public async Task EnsureIsAdminAsync(int userId, int chatId)
        {
            if (!await IsAdminAsync(userId, chatId))
            {
                logger.LogWarning("Требуются права администратора: пользователь {UserId}, чат {ChatId}", userId, chatId);
                throw new UnauthorizedAccessException("Требуются права администратора");
            }
        }

        private async Task<ChatMember?> GetMembershipAsync(int userId, int chatId)
        {
            var key = (userId, chatId);

            if (_requestCache.TryGetValue(key, out var requestCached))
            {
                return requestCached;
            }

            var member = await cache.GetMembershipAsync(userId, chatId, async () =>
            {
                return await context.ChatMembers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ChatId == chatId);
            });

            _requestCache[key] = member;

            logger.LogDebug("Членство пользователя {UserId} в чате {ChatId}: {Role}",
                userId, chatId, member?.Role.ToString() ?? "не состоит");

            return member;
        }
    }
}