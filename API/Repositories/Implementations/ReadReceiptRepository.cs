using API.Repositories.Abstarctions;

namespace API.Repositories.Implementations;

public sealed class ReadReceiptRepository(MessengerDbContext context) : IReadReceiptRepository
{
    public Task<ChatMember?> FindMemberAsync(int chatId, int userId, CancellationToken ct = default)
        => context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId, ct);
    public Task<ChatMember?> FindMemberReadonlyAsync(int chatId, int userId, CancellationToken ct = default)
        => context.ChatMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId, ct);
    public Task<int> CountUnreadAsync(int chatId, int userId, int lastReadMessageId, CancellationToken ct = default)
        => context.UserMessages.CountAsync(m => m.ChatId == chatId && m.Id > lastReadMessageId && m.IsDeleted != true && m.SenderId != userId, ct);

    public async Task<Dictionary<int, int>> GetUnreadCountsAsync(int userId, IEnumerable<int> chatIds, CancellationToken ct = default)
    {
        var ids = chatIds.ToList();

        var result = await context.ChatMembers.Where(cm => cm.UserId == userId && ids.Contains(cm.ChatId)).Select(cm => new
        {
            cm.ChatId,
            Count = context.UserMessages.Count(m => m.ChatId == cm.ChatId && m.Id > (cm.LastReadMessageId ?? 0) && m.IsDeleted != true && m.SenderId != userId)
        }).AsNoTracking().ToDictionaryAsync(x => x.ChatId, x => x.Count, ct);

        foreach (var id in ids)
            result.TryAdd(id, 0);

        return result;
    }

    public async Task<List<UnreadCountProjection>> GetAllUnreadCountsAsync(int userId, CancellationToken ct = default)
    => await context.ChatMembers
        .Where(cm => cm.UserId == userId)
        .Select(cm => new
        {
            cm.ChatId,
            UnreadCount = context.UserMessages.Count(m =>
                m.ChatId == cm.ChatId
                && m.Id > (cm.LastReadMessageId ?? 0)
                && m.IsDeleted != true
                && m.SenderId != userId)
        })
        .Where(x => x.UnreadCount > 0)
        .Select(x => new UnreadCountProjection(x.ChatId, x.UnreadCount))
        .AsNoTracking()
        .ToListAsync(ct);

    public async Task UpdateReadPointerAsync(ChatMember member, int messageId, DateTime readAt, CancellationToken ct = default)
    {
        member.LastReadMessageId = messageId;
        member.LastReadAt = readAt;
    }

    public Task<bool> MessageExistsAsync(int messageId, int chatId, CancellationToken ct = default)
        => context.Messages.AnyAsync(m => m.Id == messageId && m.ChatId == chatId && m.IsDeleted != true, ct);

    public Task<int> GetLastMessageIdAsync(int chatId, CancellationToken ct = default)
        => context.Messages.Where(m => m.ChatId == chatId && m.IsDeleted != true).OrderByDescending(m => m.Id).Select(m => m.Id).FirstOrDefaultAsync(ct);

    public async Task<UnreadInfoProjection?> GetUnreadInfoAsync(int chatId, int userId, int lastReadMessageId, CancellationToken ct = default)
    {
        var result = await context.UserMessages.Where(m => m.ChatId == chatId && m.Id > lastReadMessageId && m.IsDeleted != true && m.SenderId != userId)
                                               .GroupBy(_ => 1).Select(g => new { Count = g.Count(), FirstId = g.Min(m => m.Id) }).FirstOrDefaultAsync(ct);

        return result is null or { Count: 0 } ? null : new UnreadInfoProjection(result.Count, result.FirstId);
    }
    public async Task<Dictionary<int, int>> GetUnreadCountsForUsersAsync(int chatId, IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.ToList();

        return await context.ChatMembers.Where(cm => cm.ChatId == chatId && ids.Contains(cm.UserId)).Select(cm => new
        {
            cm.UserId,
            Count = context.UserMessages.Count(m => m.ChatId == chatId && m.Id > (cm.LastReadMessageId ?? 0) && m.IsDeleted != true && m.SenderId != cm.UserId)
        }).AsNoTracking().ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
    }
}