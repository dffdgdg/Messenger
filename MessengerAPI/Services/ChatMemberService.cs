using MessengerAPI.Model;
using MessengerShared.DTO.Chat;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IChatMemberService
    {
        Task<ChatMemberDTO> AddMemberAsync(int chatId, int userId, int addedByUserId, ChatRole role = ChatRole.Member);
        Task RemoveMemberAsync(int chatId, int userId, int removedByUserId);
        Task<ChatMemberDTO> UpdateRoleAsync(int chatId, int userId, ChatRole newRole, int updatedByUserId);
        Task<List<ChatMemberDTO>> GetMembersAsync(int chatId);
        Task LeaveAsync(int chatId, int userId);
    }

    public class ChatMemberService(MessengerDbContext context,ICacheService cache,IAccessControlService accessControl,ILogger<ChatMemberService> logger)
        : BaseService<ChatMemberService>(context, logger), IChatMemberService
    {
        public async Task<ChatMemberDTO> AddMemberAsync(int chatId, int userId, int addedByUserId, ChatRole role = ChatRole.Member)
        {
            // Проверяем права
            await accessControl.EnsureIsAdminAsync(addedByUserId, chatId);

            // Проверяем, не является ли уже участником
            var exists = await _context.ChatMembers.AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

            if (exists)
                throw new InvalidOperationException("Пользователь уже является участником чата");

            var member = new ChatMember
            {
                ChatId = chatId,
                UserId = userId,
                Role = role,
                JoinedAt = DateTime.UtcNow,
                NotificationsEnabled = true
            };

            _context.ChatMembers.Add(member);
            await SaveChangesAsync();

            // Инвалидируем кэш
            cache.InvalidateUserChats(userId);
            cache.InvalidateMembership(userId, chatId);

            _logger.LogInformation(
                "Пользователь {UserId} добавлен в чат {ChatId} пользователем {AddedBy}",
                userId, chatId, addedByUserId);

            return MapToDto(member);
        }

        public async Task RemoveMemberAsync(int chatId, int userId, int removedByUserId)
        {
            // Проверяем права (если удаляет не себя)
            if (userId != removedByUserId)
            {
                await accessControl.EnsureIsAdminAsync(removedByUserId, chatId);
            }

            var member = await _context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId)
                ?? throw new KeyNotFoundException("Пользователь не является участником чата");

            // Владелец не может быть удалён
            if (member.Role == ChatRole.Owner)
                throw new InvalidOperationException("Невозможно удалить владельца чата");

            _context.ChatMembers.Remove(member);
            await SaveChangesAsync();

            // Инвалидируем кэш
            cache.InvalidateUserChats(userId);
            cache.InvalidateMembership(userId, chatId);

            _logger.LogInformation(
                "Пользователь {UserId} удалён из чата {ChatId} пользователем {RemovedBy}",
                userId, chatId, removedByUserId);
        }

        public async Task<ChatMemberDTO> UpdateRoleAsync(int chatId, int userId, ChatRole newRole, int updatedByUserId)
        {
            await accessControl.EnsureIsOwnerAsync(updatedByUserId, chatId);

            var member = await _context.ChatMembers
                .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId)
                ?? throw new KeyNotFoundException("Пользователь не является участником чата");

            if (member.Role == ChatRole.Owner)
                throw new InvalidOperationException("Невозможно изменить роль владельца");

            if (newRole == ChatRole.Owner)
                throw new InvalidOperationException("Используйте отдельный метод для передачи владения");

            member.Role = newRole;
            await SaveChangesAsync();

            // Инвалидируем кэш членства
            cache.InvalidateMembership(userId, chatId);

            _logger.LogInformation("Роль пользователя {UserId} в чате {ChatId} изменена на {Role}",userId, chatId, newRole);

            return MapToDto(member);
        }

        public async Task<List<ChatMemberDTO>> GetMembersAsync(int chatId)
        {
            var members = await _context.ChatMembers.Where(cm => cm.ChatId == chatId).Include(cm => cm.User).AsNoTracking().ToListAsync();

            return members.ConvertAll(MapToDto);
        }

        public async Task LeaveAsync(int chatId, int userId) => await RemoveMemberAsync(chatId, userId, userId);

        private static ChatMemberDTO MapToDto(ChatMember member) => new()
        {
            ChatId = member.ChatId,
            UserId = member.UserId,
            Role = member.Role,
            JoinedAt = member.JoinedAt,
            NotificationsEnabled = member.NotificationsEnabled
        };
    }
}