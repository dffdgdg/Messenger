using API.Repositories.Abstarctions;
using API.Repositories.Base;

namespace API.Repositories.Implementations;

public sealed class MessageRepository(MessengerDbContext context)
    : RepositoryBase<Message>(context), IMessageRepository
{
    public Task<UserMessage?> FindUserMessageByIdAsync(int messageId, CancellationToken ct = default)
        => _context.UserMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public Task<UserMessage?> FindUserMessageWithIncludesAsync(int messageId, CancellationToken ct = default)
        => WithFullIncludes().FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public Task<UserMessage?> FindUserMessageForDeleteAsync(int messageId, CancellationToken ct = default)
        => _context.UserMessages.Include(m => m.VoiceMessage).Include(m => m.MessageFiles).FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public async Task<List<UserMessage>> GetBeforeAsync(int chatId, int beforeId, int take, DateTime? cutoff = null, CancellationToken ct = default)
    {
        var q = LightQuery().Where(m => m.ChatId == chatId && m.Id < beforeId && m.IsDeleted != true);
        if (cutoff.HasValue)
            q = q.Where(m => m.CreatedAt >= cutoff.Value);
        return await q.OrderByDescending(m => m.Id).Take(take).AsNoTracking().ToListAsync(ct);
    }

    public async Task<List<UserMessage>> GetAfterAsync(int chatId, int afterId, int take, DateTime? cutoff = null, CancellationToken ct = default)
    {
        var q = LightQuery().Where(m => m.ChatId == chatId && m.Id > afterId && m.IsDeleted != true);
        if (cutoff.HasValue)
            q = q.Where(m => m.CreatedAt >= cutoff.Value);
        return await q.OrderBy(m => m.Id).Take(take).AsNoTracking().ToListAsync(ct);
    }

    public async Task<List<UserMessage>> GetUserMessagesForMixedAsync(int chatId, int? beforeId, int? afterId, DateTime? cutoff, CancellationToken ct = default)
    {
        var q = _context.UserMessages
            .Include(m => m.Sender)
            .Include(m => m.VoiceMessage)
            .Include(m => m.MessageFiles)
            .Include(m => m.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes)
            .Include(m => m.ReplyToMessage)
            .Where(m => m.ChatId == chatId && m.IsDeleted != true)
            .AsNoTracking();

        if (beforeId.HasValue) q = q.Where(m => m.Id < beforeId.Value);
        if (afterId.HasValue) q = q.Where(m => m.Id > afterId.Value);
        if (cutoff.HasValue) q = q.Where(m => m.CreatedAt >= cutoff.Value);

        return await q.ToListAsync(ct);
    }

    public async Task<List<SystemMessage>> GetSystemMessagesAsync(int chatId, int? beforeId, int? afterId, DateTime? cutoff,
        CancellationToken ct = default)
    {
        var q = _context.SystemMessages
            .Include(m => m.Initiator)
            .Include(m => m.TargetUser)
            .Where(m => m.ChatId == chatId && m.IsDeleted != true)
            .AsNoTracking();

        if (beforeId.HasValue) q = q.Where(m => m.Id < beforeId.Value);
        if (afterId.HasValue) q = q.Where(m => m.Id > afterId.Value);
        if (cutoff.HasValue) q = q.Where(m => m.CreatedAt >= cutoff.Value);

        return await q.ToListAsync(ct);
    }

    public async Task<List<UserMessage>> GetPinnedAsync(int chatId, DateTime? cutoff = null, CancellationToken ct = default)
    {
        var q = LightQuery().Where(m => m.ChatId == chatId && m.PinnedAt != null && m.IsDeleted != true);
        if (cutoff.HasValue)
            q = q.Where(m => m.CreatedAt >= cutoff.Value);
        return await q.OrderByDescending(m => m.PinnedAt).AsNoTracking().ToListAsync(ct);
    }

    public Task<int> CountAsync(int chatId, DateTime? cutoff, CancellationToken ct = default)
    {
        var q = _context.Messages.Where(m => m.ChatId == chatId && m.IsDeleted != true);
        if (cutoff.HasValue)
            q = q.Where(m => m.CreatedAt >= cutoff.Value);
        return q.CountAsync(ct);
    }

    public Task<bool> HasOlderAsync(int chatId, int beforeId, DateTime? cutoff, CancellationToken ct = default)
    {
        var q = _context.Messages.Where(m => m.ChatId == chatId && m.Id < beforeId && m.IsDeleted != true);
        if (cutoff.HasValue)
            q = q.Where(m => m.CreatedAt >= cutoff.Value);
        return q.AnyAsync(ct);
    }

    public Task<bool> HasNewerAsync(int chatId, int afterId, DateTime? cutoff, CancellationToken ct = default)
    {
        var q = _context.Messages.Where(m => m.ChatId == chatId && m.Id > afterId && m.IsDeleted != true);
        if (cutoff.HasValue)
            q = q.Where(m => m.CreatedAt >= cutoff.Value);
        return q.AnyAsync(ct);
    }

    public Task<bool> ExistsInChatAsync(int messageId, int chatId, CancellationToken ct = default)
        => _context.UserMessages.AnyAsync(m => m.Id == messageId&& m.ChatId == chatId&& m.IsDeleted != true, ct);

    public new Task<bool> ExistsAsync(int messageId, CancellationToken ct = default)
        => _context.UserMessages.AnyAsync(m => m.Id == messageId && m.IsDeleted != true, ct);

    public async Task<(List<UserMessage> Items, int Total)> SearchInChatAsync(
        int chatId, string escapedQuery,
        int? senderId, DateTime? dateFrom, DateTime? dateTo,
        bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText,
        bool oldestFirst, int page, int pageSize, DateTime? cutoff,
        CancellationToken ct = default)
    {
        var q = WithFullIncludes().Where(m => m.ChatId == chatId && m.IsDeleted != true
            && (string.IsNullOrEmpty(escapedQuery) || (m.Content != null && EF.Functions.ILike(m.Content, $"%{escapedQuery}%")))).AsNoTracking();

        if (cutoff.HasValue) q = q.Where(m => m.CreatedAt >= cutoff.Value);
        if (senderId.HasValue) q = q.Where(m => m.SenderId == senderId.Value);
        if (dateFrom.HasValue) q = q.Where(m => m.CreatedAt >= dateFrom.Value);
        if (dateTo.HasValue) q = q.Where(m => m.CreatedAt < dateTo.Value.AddDays(1));
        if (hasFiles) q = q.Where(m => m.MessageFiles.Any());
        if (hasVoice) q = q.Where(m => m.VoiceMessage != null);
        if (hasPoll) q = q.Where(m => m.Poll != null);
        if (onlyText) q = q.Where(m => !m.MessageFiles.Any() && m.VoiceMessage == null && m.Poll == null);

        q = oldestFirst ? q.OrderBy(m => m.CreatedAt) : q.OrderByDescending(m => m.CreatedAt);

        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(List<UserMessage> Items, int Total)> SearchGlobalAsync(
        IEnumerable<int> chatIds, string escapedQuery,
        int? senderId, DateTime? dateFrom, DateTime? dateTo,
        bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText,
        bool oldestFirst, int page, int pageSize,
        Dictionary<int, DateTime> historyFilter,
        CancellationToken ct = default)
    {
        var ids = chatIds.ToList();
        var q = _context.UserMessages
            .Include(m => m.Sender)
            .Include(m => m.Chat)
            .Include(m => m.MessageFiles)
            .Include(m => m.VoiceMessage)
            .Include(m => m.Poll)
            .Where(m => ids.Contains(m.ChatId) && m.IsDeleted != true && (string.IsNullOrEmpty(escapedQuery)
                || (m.Content != null && EF.Functions.ILike(m.Content, $"%{escapedQuery}%"))))
            .AsNoTracking();

        if (senderId.HasValue) q = q.Where(m => m.SenderId == senderId.Value);
        if (dateFrom.HasValue) q = q.Where(m => m.CreatedAt >= dateFrom.Value);
        if (dateTo.HasValue) q = q.Where(m => m.CreatedAt < dateTo.Value.AddDays(1));
        if (hasFiles) q = q.Where(m => m.MessageFiles.Any());
        if (hasVoice) q = q.Where(m => m.VoiceMessage != null);
        if (hasPoll) q = q.Where(m => m.Poll != null);
        if (onlyText) q = q.Where(m => !m.MessageFiles.Any() && m.VoiceMessage == null && m.Poll == null);

        if (historyFilter.Count > 0)
        {
            var restrictedIds = historyFilter.Keys.ToList();
            q = q.Where(m => !restrictedIds.Contains(m.ChatId)
                           || m.CreatedAt >= historyFilter[m.ChatId]);
        }

        q = oldestFirst ? q.OrderBy(m => m.CreatedAt) : q.OrderByDescending(m => m.CreatedAt);

        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<List<int>> GetForwardedToChatIdsAsync(int originalMessageId, CancellationToken ct = default)
        => _context.UserMessages.Where(m => m.Id == originalMessageId || m.ForwardedFromMessageId == originalMessageId)
            .Select(m => m.ChatId).Distinct().ToListAsync(ct);

    public Task<int> SoftDeleteAsync(int messageId, DateTime editedAt, CancellationToken ct = default)
        => _context.UserMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDeleted, true).SetProperty(m => m.Content, (string?)null).SetProperty(m => m.EditedAt, editedAt), ct);

    public Task<int> PinAsync(int messageId, int pinnedByUserId, DateTime pinnedAt, CancellationToken ct = default)
        => _context.Messages.Where(m => m.Id == messageId).ExecuteUpdateAsync(s => s.SetProperty(m => m.PinnedAt, pinnedAt).SetProperty(m => m.PinnedByUserId, pinnedByUserId), ct);

    public Task<int> UnpinAsync(int messageId, CancellationToken ct = default)
        => _context.Messages.Where(m => m.Id == messageId).ExecuteUpdateAsync(s => s.SetProperty(m => m.PinnedAt, (DateTime?)null).SetProperty(m => m.PinnedByUserId, (int?)null), ct);

    public void Add(UserMessage message)
        => _context.UserMessages.Add(message);

    public void RemoveVoiceMessage(VoiceMessage voiceMessage)
        => _context.VoiceMessages.Remove(voiceMessage);

    private IQueryable<UserMessage> WithFullIncludes()
        => _context.UserMessages
            .Include(m => m.Sender)
            .Include(m => m.VoiceMessage)
            .Include(m => m.MessageFiles)
            .Include(m => m.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes)
            .Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender)
            .Include(m => m.ReplyToMessage).ThenInclude(r => r!.VoiceMessage)
            .Include(m => m.ReplyToMessage).ThenInclude(r => r!.MessageFiles)
            .Include(m => m.ReplyToMessage).ThenInclude(r => r!.Poll)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.VoiceMessage)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.MessageFiles)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes);

    private IQueryable<UserMessage> LightQuery()
        => _context.UserMessages
            .Include(m => m.Sender)
            .Include(m => m.MessageFiles)
            .Include(m => m.VoiceMessage)
            .Include(m => m.Poll)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.VoiceMessage)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.MessageFiles)
            .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes);
}