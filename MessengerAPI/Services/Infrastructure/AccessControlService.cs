namespace MessengerAPI.Services.Infrastructure;

public interface IAccessControlService
{
    Task<bool> IsMemberAsync(int userId, int chatId);
    Task<bool> IsOwnerAsync(int userId, int chatId);
    Task<bool> IsAdminAsync(int userId, int chatId);
    Task<ChatRole?> GetRoleAsync(int userId, int chatId);
    Task<Result> CheckIsMemberAsync(int userId, int chatId);
    Task<Result> CheckIsOwnerAsync(int userId, int chatId);
    Task<Result> CheckIsAdminAsync(int userId, int chatId);
    Task<List<int>> GetUserChatIdsAsync(int userId);
}

public sealed class AccessControlService(MessengerDbContext context, ICacheService cache, ILogger<AccessControlService> logger)
    : IAccessControlService
{
    private readonly Dictionary<(int UserId, int ChatId), ChatMember?> _requestCache = [];

    public async Task<List<int>> GetUserChatIdsAsync(int userId)
    {
        return await cache.GetUserChatIdsAsync(userId, () =>
            context.ChatMembers.Where(cm => cm.UserId == userId).Select(cm => cm.ChatId).ToListAsync());
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

    public async Task<Result> CheckIsMemberAsync(int userId, int chatId)
    {
        if (await IsMemberAsync(userId, chatId))
            return Result.Success();

        logger.LogWarning("Доступ запрещён: пользователь {UserId} к чату {ChatId}", userId, chatId);
        return Result.Forbidden("У вас нет доступа к этому чату");
    }

    public async Task<Result> CheckIsOwnerAsync(int userId, int chatId)
    {
        if (await IsOwnerAsync(userId, chatId))
            return Result.Success();

        logger.LogWarning("Требуются права владельца: пользователь {UserId}, чат {ChatId}", userId, chatId);
        return Result.Forbidden("Только владелец чата может выполнить это действие");
    }

    public async Task<Result> CheckIsAdminAsync(int userId, int chatId)
    {
        if (await IsAdminAsync(userId, chatId))
            return Result.Success();

        logger.LogWarning("Требуются права администратора: пользователь {UserId}, чат {ChatId}", userId, chatId);
        return Result.Forbidden("Требуются права администратора");
    }

    private async Task<ChatMember?> GetMembershipAsync(int userId, int chatId)
    {
        var key = (userId, chatId);

        if (_requestCache.TryGetValue(key, out var requestCached))
            return requestCached;

        var member = await cache.GetMembershipAsync(userId, chatId, () =>
            context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ChatId == chatId));

        _requestCache[key] = member;

        logger.LogDebug("Членство пользователя {UserId} в чате {ChatId}: {Role}", userId, chatId, member?.Role.ToString() ?? "не состоит");

        return member;
    }
}