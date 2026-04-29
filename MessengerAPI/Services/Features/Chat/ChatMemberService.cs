using MessengerAPI.Services.Base;
using MessengerAPI.Services.Infrastructure.Security;

namespace MessengerAPI.Services.Chat;

public sealed partial class ChatMemberService(MessengerDbContext context, ICacheService cache, IAccessControlService accessControl, ISystemMessageService systemMessages,
    AppDateTime appDateTime, ILogger<ChatMemberService> logger) : BaseService<ChatMemberService>(context, logger), IChatMemberService
{
    public async Task<Result<ChatMemberDto>> AddMemberAsync(int chatId, int userId, int addedByUserId, ChatRole role = ChatRole.Member)
    {
        var admin = await accessControl.EnsureAdminOfAsync(addedByUserId, chatId);
        if (admin.IsFailure) return admin.As<ChatMemberDto>();

        var exists = await _context.ChatMembers.AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (exists)
            return Result<ChatMemberDto>.Conflict("Пользователь уже является участником чата");

        var member = new ChatMember
        {
            ChatId = chatId,
            UserId = userId,
            Role = role,
            JoinedAt = appDateTime.UtcNow,
            NotificationsEnabled = true
        };

        _context.ChatMembers.Add(member);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<ChatMemberDto>();

        cache.InvalidateUserChats(userId);
        cache.InvalidateMembership(userId, chatId);

        LogMemberAdded(userId, chatId, addedByUserId);

        await systemMessages.CreateAsync(chatId, addedByUserId, SystemEventType.MemberAdded, userId);

        return Result<ChatMemberDto>.Success(MapToDto(member));
    }

    public async Task<Result> RemoveMemberAsync(int chatId, int userId, int removedByUserId)
    {
        if (userId != removedByUserId)
        {
            var admin = await accessControl.EnsureAdminOfAsync(removedByUserId, chatId);
            if (admin.IsFailure) return admin;
        }

        var member = await _context.ChatMembers.FirstOrDefaultAsync(cm =>
            cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return Result.NotFound("Пользователь не является участником чата");

        if (member.Role == ChatRole.Owner)
            return Result.Forbidden("Невозможно удалить владельца чата");

        _context.ChatMembers.Remove(member);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save;

        cache.InvalidateUserChats(userId);
        cache.InvalidateMembership(userId, chatId);

        LogMemberRemoved(userId, chatId, removedByUserId);

        if (userId == removedByUserId)
            await systemMessages.CreateAsync(chatId, userId, SystemEventType.MemberLeft);
        else
            await systemMessages.CreateAsync(chatId, removedByUserId, SystemEventType.MemberRemoved, userId);

        return Result.Success();
    }

    public async Task<Result<ChatMemberDto>> UpdateRoleAsync(int chatId, int userId, ChatRole newRole, int updatedByUserId)
    {
        var owner = await accessControl.EnsureOwnerOfAsync(updatedByUserId, chatId);
        if (owner.IsFailure) return owner.As<ChatMemberDto>();

        var member = await _context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (member is null)
            return Result<ChatMemberDto>.NotFound("Пользователь не является участником чата");

        if (member.Role == ChatRole.Owner)
            return Result<ChatMemberDto>.Forbidden("Невозможно изменить роль владельца");

        if (newRole == ChatRole.Owner)
            return Result<ChatMemberDto>.Failure("Используйте отдельный метод для передачи владения");

        member.Role = newRole;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<ChatMemberDto>();

        cache.InvalidateMembership(userId, chatId);

        LogRoleUpdated(userId, chatId, newRole);

        await systemMessages.CreateAsync(chatId, updatedByUserId, SystemEventType.RoleChanged, userId);

        return Result<ChatMemberDto>.Success(MapToDto(member));
    }

    public async Task<Result<List<ChatMemberDto>>> GetMembersAsync(int chatId, int userId)
    {
        var access = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure) return access.As<List<ChatMemberDto>>();

        var members = await _context.ChatMembers.Where(cm => cm.ChatId == chatId).Include(cm => cm.User).AsNoTracking().ToListAsync();

        return Result<List<ChatMemberDto>>.Success(members.ConvertAll(MapToDto));
    }

    public async Task<Result> LeaveAsync(int chatId, int userId)
        => await RemoveMemberAsync(chatId, userId, userId);

    private static ChatMemberDto MapToDto(ChatMember member) => new()
    {
        ChatId = member.ChatId,
        UserId = member.UserId,
        Role = member.Role,
        JoinedAt = member.JoinedAt,
        NotificationsEnabled = member.NotificationsEnabled
    };

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} добавлен в чат {ChatId} пользователем {AddedBy}")]
    private partial void LogMemberAdded(int userId, int chatId, int addedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} удалён из чата {ChatId} пользователем {RemovedBy}")]
    private partial void LogMemberRemoved(int userId, int chatId, int removedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Роль пользователя {UserId} в чате {ChatId} изменена на {Role}")]
    private partial void LogRoleUpdated(int userId, int chatId, ChatRole role);

    #endregion
}