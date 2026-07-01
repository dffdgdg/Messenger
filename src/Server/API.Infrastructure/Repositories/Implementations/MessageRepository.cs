using API.Domain.Entities;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Message;

namespace API.Infrastructure.Repositories.Implementations;

public sealed class MessageRepository(MessengerDbContext context)
    : RepositoryBase<Message>(context), IMessageRepository
{
    private IQueryable<UserMessage> FullIncludes()
        => _context.UserMessages.Include(m => m.Sender).Include(m => m.VoiceMessage).Include(m => m.MessageFiles).Include(m => m.Poll)
        .ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes).Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender)
        .Include(m => m.ReplyToMessage).ThenInclude(r => r!.VoiceMessage).Include(m => m.ReplyToMessage).ThenInclude(r => r!.MessageFiles)
        .Include(m => m.ReplyToMessage).ThenInclude(r => r!.Poll).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.VoiceMessage).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.MessageFiles)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes);

    private IQueryable<UserMessage> ListIncludes()
        => _context.UserMessages.Include(m => m.Sender).Include(m => m.VoiceMessage).Include(m => m.MessageFiles).Include(m => m.Poll)
        .ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes).Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender)
        .Include(m => m.ReplyToMessage).ThenInclude(r => r!.VoiceMessage).Include(m => m.ReplyToMessage).ThenInclude(r => r!.MessageFiles)
        .Include(m => m.ReplyToMessage).ThenInclude(r => r!.Poll);

    private IQueryable<UserMessage> PinnedIncludes()
        => _context.UserMessages.Include(m => m.Sender).Include(m => m.MessageFiles).Include(m => m.VoiceMessage).Include(m => m.Poll)
        .ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.VoiceMessage).Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.MessageFiles)
        .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Poll).ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes);

    public void Add(UserMessage message)
        => _context.UserMessages.Add(message);

    public void Add(SystemMessage message)
        => _context.SystemMessages.Add(message);

    public void RemoveVoiceMessage(VoiceMessage voiceMessage)
        => _context.VoiceMessages.Remove(voiceMessage);

    public Task<UserMessage?> FindUserMessageByIdAsync(int messageId, CancellationToken ct = default)
        => _context.UserMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public Task<UserMessage?> FindUserMessageWithIncludesAsync(int messageId, CancellationToken ct = default)
        => FullIncludes().FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public Task<UserMessage?> FindUserMessageWithIncludesNoTrackingAsync(int messageId, CancellationToken ct = default)
        => FullIncludes().AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public Task<UserMessage?> FindUserMessageForDeleteAsync(int messageId, CancellationToken ct = default)
        => _context.UserMessages.Include(m => m.VoiceMessage).Include(m => m.MessageFiles).FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public Task<SystemMessage?> FindSystemMessageWithIncludesAsync(int messageId, CancellationToken ct = default)
        => _context.SystemMessages.Include(m => m.Initiator).Include(m => m.TargetUser).AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct);
    
    public Task<MessageFile?> FindFileForDownloadAsync(int fileId, CancellationToken ct = default)
        => _context.Set<MessageFile>().Include(f => f.Message).AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
    public Task<VoiceMessage?> FindVoiceForDownloadAsync(int messageId, CancellationToken ct = default)
        => _context.VoiceMessages.Include(v => v.Message).AsNoTracking().FirstOrDefaultAsync(v => v.MessageId == messageId, ct);
    // ─── Навигация по истории ────────────────────────────────────────────────

    public async Task<(List<Message> Messages, bool HasOlder)> GetLatestAsync(int chatId, int take, DateTime? cutoff = null, CancellationToken ct = default)
    {
        var limit = take + 1;

        var messageIds = await _context.Messages.Where(m => m.ChatId == chatId && m.IsDeleted != true)
            .Where(m => cutoff == null || m.CreatedAt >= cutoff.Value).OrderByDescending(m => m.Id).Take(limit)
            .Select(m => new { m.Id, IsUser = m is UserMessage }).AsNoTracking().ToListAsync(ct);

        var hasOlder = messageIds.Count > take;
        if (hasOlder) messageIds.RemoveAt(messageIds.Count - 1);

        if (messageIds.Count == 0)
            return ([], false);

        var ids = messageIds.ConvertAll(x => x.Id);
        var userIds = messageIds.Where(x => x.IsUser).Select(x => x.Id).ToList();
        var sysIds = messageIds.Where(x => !x.IsUser).Select(x => x.Id).ToList();

        var result = await FetchMessagesAsync(ids, userIds, sysIds, useFullIncludes: false, ct);
        return (result, hasOlder);
    }

    public async Task<List<Message>> GetBeforeAsync(int chatId, int beforeId, int take, DateTime? cutoff = null, CancellationToken ct = default)
    {
        var messageIds = await _context.Messages.Where(m => m.ChatId == chatId && m.Id < beforeId && m.IsDeleted != true)
            .Where(m => cutoff == null || m.CreatedAt >= cutoff.Value).OrderByDescending(m => m.Id).Take(take)
            .Select(m => new { m.Id, IsUser = m is UserMessage }).AsNoTracking().ToListAsync(ct);

        if (messageIds.Count == 0) return [];

        var ids = messageIds.ConvertAll(x => x.Id);
        var userIds = messageIds.Where(x => x.IsUser).Select(x => x.Id).ToList();
        var sysIds = messageIds.Where(x => !x.IsUser).Select(x => x.Id).ToList();

        return await FetchMessagesAsync(ids, userIds, sysIds, useFullIncludes: true, ct);
    }

    public async Task<List<Message>> GetAfterAsync(int chatId, int afterId, int take, DateTime? cutoff = null, CancellationToken ct = default)
    {
        var messageIds = await _context.Messages.Where(m => m.ChatId == chatId && m.Id > afterId && m.IsDeleted != true)
            .Where(m => cutoff == null || m.CreatedAt >= cutoff.Value).OrderBy(m => m.Id).Take(take)
            .Select(m => new { m.Id, IsUser = m is UserMessage }).AsNoTracking().ToListAsync(ct);

        if (messageIds.Count == 0) return [];

        var ids = messageIds.ConvertAll(x => x.Id);
        var userIds = messageIds.Where(x => x.IsUser).Select(x => x.Id).ToList();
        var sysIds = messageIds.Where(x => !x.IsUser).Select(x => x.Id).ToList();

        return await FetchMessagesAsync(ids, userIds, sysIds, useFullIncludes: true, ct);
    }

    public async Task<List<UserMessage>> GetUserMessagesForMixedAsync(int chatId, int? beforeId, int? afterId, DateTime? cutoff, CancellationToken ct = default)
    {
        var q = ListIncludes().Where(m => m.ChatId == chatId && m.IsDeleted != true).AsNoTracking();

        if (beforeId.HasValue) q = q.Where(m => m.Id < beforeId.Value);
        if (afterId.HasValue) q = q.Where(m => m.Id > afterId.Value);
        if (cutoff.HasValue) q = q.Where(m => m.CreatedAt >= cutoff.Value);

        return await q.ToListAsync(ct);
    }

    public async Task<List<SystemMessage>> GetSystemMessagesAsync(int chatId, int? beforeId, int? afterId, DateTime? cutoff, CancellationToken ct = default)
    {
        var q = _context.SystemMessages.Include(m => m.Initiator).Include(m => m.TargetUser).Where(m => m.ChatId == chatId && m.IsDeleted != true).AsNoTracking();

        if (beforeId.HasValue) q = q.Where(m => m.Id < beforeId.Value);
        if (afterId.HasValue) q = q.Where(m => m.Id > afterId.Value);
        if (cutoff.HasValue) q = q.Where(m => m.CreatedAt >= cutoff.Value);

        q = beforeId.HasValue ? q.OrderByDescending(m => m.Id).Take(100) : q.OrderBy(m => m.Id).Take(100);

        return await q.ToListAsync(ct);
    }

    public async Task<List<UserMessage>> GetPinnedAsync(int chatId, DateTime? cutoff = null, CancellationToken ct = default)
    {
        var q = PinnedIncludes().Where(m => m.ChatId == chatId && m.PinnedAt != null && m.IsDeleted != true);

        if (cutoff.HasValue)
            q = q.Where(m => m.CreatedAt >= cutoff.Value);

        return await q.OrderByDescending(m => m.PinnedAt).AsNoTracking().ToListAsync(ct);
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
        => _context.UserMessages.AnyAsync(m => m.Id == messageId && m.ChatId == chatId && m.IsDeleted != true, ct);

    public new Task<bool> ExistsAsync(int messageId, CancellationToken ct = default)
        => _context.UserMessages.AnyAsync(m => m.Id == messageId && m.IsDeleted != true, ct);

    public Task<List<int>> GetForwardedToChatIdsAsync(int originalMessageId, CancellationToken ct = default)
        => _context.UserMessages.Where(m => m.Id == originalMessageId || m.ForwardedFromMessageId == originalMessageId)
            .Select(m => m.ChatId).Distinct().ToListAsync(ct);

    public Task<int> SoftDeleteAsync(int messageId, DateTime editedAt, CancellationToken ct = default)
        => _context.UserMessages.Where(m => m.Id == messageId).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDeleted, true)
            .SetProperty(m => m.Content, (string?)null).SetProperty(m => m.EditedAt, editedAt)
            .SetProperty(m => m.ReplyToMessageId, (int?)null).SetProperty(m => m.ForwardedFromMessageId, (int?)null), ct);

    public Task<int> PinAsync(int messageId, int pinnedByUserId, DateTime pinnedAt, CancellationToken ct = default)
        => _context.Messages.Where(m => m.Id == messageId).ExecuteUpdateAsync(s => s.SetProperty(m => m.PinnedAt, pinnedAt).SetProperty(m => m.PinnedByUserId, pinnedByUserId), ct);

    public Task<int> UnpinAsync(int messageId, CancellationToken ct = default)
        => _context.Messages.Where(m => m.Id == messageId).ExecuteUpdateAsync(s => s.SetProperty(m => m.PinnedAt, (DateTime?)null).SetProperty(m => m.PinnedByUserId, (int?)null), ct);

    public async Task<(List<UserMessage> Items, int Total)> SearchInChatAsync(int chatId, string escapedQuery, int? senderId, DateTime? dateFrom,
        DateTime? dateTo, bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText, bool oldestFirst, int page, int pageSize, DateTime? cutoff,
        CancellationToken ct = default)
    {
        var q = FullIncludes().Where(m => m.ChatId == chatId && m.IsDeleted != true && (string.IsNullOrEmpty(escapedQuery)
            || (m.Content != null&& EF.Functions.ILike(m.Content, $"%{escapedQuery}%")))).AsNoTracking();

        q = ApplyMessageFilters(q, senderId, dateFrom, dateTo, hasFiles, hasVoice, hasPoll, onlyText, cutoff);

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

        var q = _context.UserMessages.Include(m => m.Sender).Include(m => m.Chat).Include(m => m.MessageFiles).Include(m => m.VoiceMessage)
            .Include(m => m.Poll).Where(m => ids.Contains(m.ChatId) && m.IsDeleted != true && (string.IsNullOrEmpty(escapedQuery)
                || (m.Content != null&& EF.Functions.ILike(m.Content, $"%{escapedQuery}%")))).AsNoTracking();

        q = ApplyMessageFilters(q, senderId, dateFrom, dateTo,hasFiles, hasVoice, hasPoll, onlyText, cutoff: null);

        if (historyFilter.Count > 0)
        {
            var restrictedIds = historyFilter.Keys.ToList();

            q = q.Where(m => !restrictedIds.Contains(m.ChatId)|| m.CreatedAt >= historyFilter.GetValueOrDefault(m.ChatId));
        }

        q = oldestFirst ? q.OrderBy(m => m.CreatedAt) : q.OrderByDescending(m => m.CreatedAt);

        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<ChatCountsDto> GetChatCountsAsync(int chatId, DateTime? cutoff)
    {
        var baseQuery = _context.UserMessages.Where(m => m.ChatId == chatId && m.IsDeleted != true && (cutoff == null || m.CreatedAt >= cutoff));

        var stats = await baseQuery.Select(m => new
        {
            HasPoll = m.Poll != null,
            IsPinned = m.PinnedAt != null,
            Files = m.MessageFiles.Select(f => f.ContentType).ToList()
        }).ToListAsync();

        var mediaCount = 0;
        var filesCount = 0;

        foreach (var m in stats)
        {
            foreach (var ct in m.Files)
            {
                if (IsImageContentType(ct)) mediaCount++;
                else filesCount++;
            }
        }

        return new ChatCountsDto
        {
            MediaCount = mediaCount,
            FilesCount = filesCount,
            PollsCount = stats.Count(m => m.HasPoll),
            PinnedCount = stats.Count(m => m.IsPinned)
        };
    }

    /// <summary>
    /// Единая точка загрузки пакета сообщений по ID.
    /// <paramref name="useFullIncludes"/> — включать ForwardedFromMessage или нет.
    /// </summary>
    private async Task<List<Message>> FetchMessagesAsync(List<int> ids, List<int> userIds, List<int> sysIds, bool useFullIncludes, CancellationToken ct)
    {
        var result = new List<Message>();

        if (userIds.Count > 0)
        {
            var userQuery = useFullIncludes ? FullIncludes() : ListIncludes();

            var userMessages = await userQuery.Where(m => userIds.Contains(m.Id)).AsNoTracking().ToListAsync(ct);

            result.AddRange(userMessages);
        }

        if (sysIds.Count > 0)
        {
            var sysMessages = await _context.SystemMessages.Include(m => m.Initiator).Include(m => m.TargetUser)
                .Where(m => sysIds.Contains(m.Id)).AsNoTracking() .ToListAsync(ct);

            result.AddRange(sysMessages);
        }

        var orderMap = ids.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);

        result.Sort((a, b) => orderMap[a.Id].CompareTo(orderMap[b.Id]));
        return result;
    }

    /// <summary>
    /// Общие фильтры для SearchInChat и SearchGlobal — убирает дублирование.
    /// </summary>
    private static IQueryable<UserMessage> ApplyMessageFilters(IQueryable<UserMessage> q,int? senderId, DateTime? dateFrom, DateTime? dateTo,
        bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText, DateTime? cutoff)
    {
        if (cutoff.HasValue) q = q.Where(m => m.CreatedAt >= cutoff.Value);
        if (senderId.HasValue) q = q.Where(m => m.SenderId == senderId.Value);
        if (dateFrom.HasValue) q = q.Where(m => m.CreatedAt >= dateFrom.Value);
        if (dateTo.HasValue) q = q.Where(m => m.CreatedAt < dateTo.Value.AddDays(1));
        if (hasFiles) q = q.Where(m => m.MessageFiles.Any());
        if (hasVoice) q = q.Where(m => m.VoiceMessage != null);
        if (hasPoll) q = q.Where(m => m.Poll != null);
        if (onlyText) q = q.Where(m => !m.MessageFiles.Any() && m.VoiceMessage == null && m.Poll == null);
        return q;
    }

    private static readonly HashSet<string> ImageContentTypes =
    [
        "image/jpeg", "image/png", "image/gif",
        "image/webp", "image/bmp", "image/avif"
    ];

    private static bool IsImageContentType(string contentType)
        => ImageContentTypes.Contains(contentType);
}