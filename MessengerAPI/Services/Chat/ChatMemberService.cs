using MessengerAPI.Common;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO.Chat;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services.Chat;

public interface IChatMemberService
{
    Task<Result<ChatMemberDTO>> AddMemberAsync(int chatId, int userId, int addedByUserId, ChatRole role = ChatRole.Member);
    Task<Result> RemoveMemberAsync(int chatId, int userId, int removedByUserId);
    Task<Result<ChatMemberDTO>> UpdateRoleAsync(int chatId, int userId, ChatRole newRole, int updatedByUserId);
    Task<Result<List<ChatMemberDTO>>> GetMembersAsync(int chatId);
    Task<Result> LeaveAsync(int chatId, int userId);
}

public class ChatMemberService(MessengerDbContext context, ICacheService cache, IAccessControlService accessControl,
    ILogger<ChatMemberService> logger) : BaseService<ChatMemberService>(context, logger), IChatMemberService
{
    public async Task<Result<ChatMemberDTO>> AddMemberAsync(int chatId, int userId, int addedByUserId, ChatRole role = ChatRole.Member)
    {
        await accessControl.EnsureIsAdminAsync(addedByUserId, chatId);

        var exists = await _context.ChatMembers.AnyAsync(cm =>
            cm.ChatId == chatId && cm.UserId == userId);

        if (exists)
            return Result<ChatMemberDTO>.Failure("Пользователь уже является участником чата");

        var member = new ChatMember
        {
            ChatId = chatId,
            UserId = userId,
            Role = role,
            JoinedAt = AppDateTime.UtcNow,
            NotificationsEnabled = true
        };

        _context.ChatMembers.Add(member);
        await SaveChangesAsync();

        cache.InvalidateUserChats(userId);
        cache.InvalidateMembership(userId, chatId);

        _logger.LogInformation("Пользователь {UserId} добавлен в чат {ChatId} пользователем {AddedBy}",userId, chatId, addedByUserId);

        return Result<ChatMemberDTO>.Success(MapToDto(member));
    }

    public async Task<Result> RemoveMemberAsync(int chatId, int userId, int removedByUserId)
    {
        if (userId != removedByUserId)
            await accessControl.EnsureIsAdminAsync(removedByUserId, chatId);

        var member = await _context.ChatMembers.FirstOrDefaultAsync(cm =>
            cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return Result.Failure("Пользователь не является участником чата");

        if (member.Role == ChatRole.Owner)
            return Result.Failure("Невозможно удалить владельца чата");

        _context.ChatMembers.Remove(member);
        await SaveChangesAsync();

        cache.InvalidateUserChats(userId);
        cache.InvalidateMembership(userId, chatId);

        _logger.LogInformation("Пользователь {UserId} удалён из чата {ChatId} пользователем {RemovedBy}", userId, chatId, removedByUserId);

        return Result.Success();
    }

    public async Task<Result<ChatMemberDTO>> UpdateRoleAsync(int chatId, int userId, ChatRole newRole, int updatedByUserId)
    {
        await accessControl.EnsureIsOwnerAsync(updatedByUserId, chatId);

        var member = await _context.ChatMembers.FirstOrDefaultAsync(cm =>
            cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return Result<ChatMemberDTO>.Failure("Пользователь не является участником чата");

        if (member.Role == ChatRole.Owner)
            return Result<ChatMemberDTO>.Failure("Невозможно изменить роль владельца");

        if (newRole == ChatRole.Owner)
            return Result<ChatMemberDTO>.Failure("Используйте отдельный метод для передачи владения");

        member.Role = newRole;
        await SaveChangesAsync();

        cache.InvalidateMembership(userId, chatId);

        _logger.LogInformation("Роль пользователя {UserId} в чате {ChatId} изменена на {Role}",userId, chatId, newRole);

        return Result<ChatMemberDTO>.Success(MapToDto(member));
    }

    public async Task<Result<List<ChatMemberDTO>>> GetMembersAsync(int chatId)
    {
        var members = await _context.ChatMembers
            .Where(cm => cm.ChatId == chatId)
            .Include(cm => cm.User)
            .AsNoTracking().ToListAsync();

        return Result<List<ChatMemberDTO>>.Success(members.ConvertAll(MapToDto));
    }

    public async Task<Result> LeaveAsync(int chatId, int userId) => await RemoveMemberAsync(chatId, userId, userId);

    private static ChatMemberDTO MapToDto(ChatMember member) => new()
    {
        ChatId = member.ChatId,
        UserId = member.UserId,
        Role = member.Role,
        JoinedAt = member.JoinedAt,
        NotificationsEnabled = member.NotificationsEnabled
    };
}