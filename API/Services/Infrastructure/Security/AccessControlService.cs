namespace API.Services.Infrastructure;

public sealed partial class AccessControlService(MessengerDbContext context, ICacheService cache,
    IHttpContextAccessor httpContextAccessor, ILogger<AccessControlService> logger) : IAccessControlService
{
    private bool? _cachedIsSystemAdmin;

    private readonly Dictionary<(int UserId, int ChatId), ChatMember?> _requestCache = [];

    public async Task<List<int>> GetUserChatIdsAsync(int userId)
        => await cache.GetUserChatIdsAsync(userId, () => context.ChatMembers.Where(cm => cm.UserId == userId).Select(cm => cm.ChatId).ToListAsync());

    public async Task<bool> IsMemberAsync(int userId, int chatId)
        => await GetMembershipAsync(userId, chatId) is not null;

    public async Task<bool> IsOwnerAsync(int userId, int chatId)
        => IsSystemAdmin() || (await GetMembershipAsync(userId, chatId))?.Role == ChatRole.Owner;

    public async Task<bool> IsAdminAsync(int userId, int chatId)
        => IsSystemAdmin() || (await GetMembershipAsync(userId, chatId))?.Role is ChatRole.Admin or ChatRole.Owner;

    public async Task<ChatRole?> GetRoleAsync(int userId, int chatId)
        => (await GetMembershipAsync(userId, chatId))?.Role;

    public async Task<ChatMember?> GetChatMemberAsync(int userId, int chatId)
        => await GetMembershipAsync(userId, chatId);

    public async Task<List<int>> GetChatMemberIdsAsync(int chatId)
        => await context.ChatMembers.Where(m => m.ChatId == chatId).Select(m => m.UserId).ToListAsync();

    public async Task<ChatType> GetChatTypeAsync(int chatId)
        => await context.Chats.Where(c => c.Id == chatId).Select(c => c.Type).FirstOrDefaultAsync();

    private async Task<ChatMember?> GetMembershipAsync(int userId, int chatId)
    {
        var key = (userId, chatId);
        if (_requestCache.TryGetValue(key, out var requestCached))
            return requestCached;

        var member = await cache.GetMembershipAsync(userId,chatId, () => context.ChatMembers.AsNoTracking()
            .FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ChatId == chatId));

        _requestCache[key] = member;
        LogMembershipResult(userId, chatId, member?.Role);
        return member;
    }

    private bool IsSystemAdmin()
    {
        if (_cachedIsSystemAdmin.HasValue)
            return _cachedIsSystemAdmin.Value;

        var isAdmin = httpContextAccessor.HttpContext?.User.IsInRole("Admin") ?? false;
        _cachedIsSystemAdmin = isAdmin;

        if (isAdmin)
            LogSystemAdminBypass();

        return isAdmin;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Членство: пользователь {UserId} в чате {ChatId} имеет роль {Role}")]
    private partial void LogMembershipResult(int userId, int chatId, ChatRole? role);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Системный администратор получил доступ через bypass")]
    private partial void LogSystemAdminBypass();
}