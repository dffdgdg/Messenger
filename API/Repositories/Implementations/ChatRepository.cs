using API.Repositories.Abstarctions;
using API.Repositories.Base;
using API.Repositories.Projections;

namespace API.Repositories.Implementations;

public sealed class ChatRepository(MessengerDbContext context) : RepositoryBase<Chat>(context), IChatRepository
{
    public async Task<Chat?> FindByIdWithMembersAsync(int chatId, CancellationToken ct = default)
        => await _context.Chats.Include(c => c.ChatMembers).FirstOrDefaultAsync(c => c.Id == chatId, ct);

    public async Task<List<Chat>> GetByIdsAsync(IEnumerable<int> chatIds, CancellationToken ct = default)
        => await _context.Chats.Where(c => chatIds.Contains(c.Id)).AsNoTracking().ToListAsync(ct);

    public async Task<Chat?> FindContactChatAsync(int userId, int contactUserId, CancellationToken ct = default)
        => await _context.Chats.Include(c => c.ChatMembers).Where(c => c.Type == ChatType.Contact&& c.ChatMembers.Any(cm => cm.UserId == userId)
              && c.ChatMembers.Any(cm => cm.UserId == contactUserId)).FirstOrDefaultAsync(ct);

    public async Task<List<int>> GetMemberIdsAsync(int chatId, CancellationToken ct = default)
        => await _context.ChatMembers.Where(cm => cm.ChatId == chatId).Select(cm => cm.UserId).ToListAsync(ct);

    public async Task<ChatMember?> GetMemberAsync(int chatId, int userId, CancellationToken ct = default)
        => await _context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId, ct);

    public async Task<bool> IsMemberAsync(int chatId, int userId, CancellationToken ct = default)
        => await _context.ChatMembers.AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId, ct);

    public void AddMember(ChatMember member)
        => _context.ChatMembers.Add(member);

    public void RemoveMember(ChatMember member)
        => _context.ChatMembers.Remove(member);

    public async Task<List<LastMessageProjection>> GetLastMessagesAsync(IEnumerable<int> chatIds, CancellationToken ct = default)
    {
        var ids = chatIds.ToList();
        if (ids.Count == 0)
            return [];

        // Одним запросом получаем ID последних сообщений
        var lastMessageIds = await _context.Messages
            .Where(m => ids.Contains(m.ChatId) && m.IsDeleted != true)
            .GroupBy(m => m.ChatId)
            .Select(g => g.Max(m => m.Id))
            .ToListAsync(ct);

        if (lastMessageIds.Count == 0)
            return [];

        // Одним запросом — UserMessages (без резолва цепочки пересылки)
        var userMessages = await _context.UserMessages
            .Where(m => lastMessageIds.Contains(m.Id))
            .Select(m => new LastMessageProjection(
                m.Id,
                m.ChatId,
                m.CreatedAt,
                false,
                m.SenderId,
                m.Content,  // <-- БЫЛО: m.ForwardedFromMessage != null ? m.ForwardedFromMessage.Content : null
                null,
                null,
                m.Sender == null ? null : (
                    (m.Sender.Surname != null ? m.Sender.Surname + " " : "") +
                    (m.Sender.Name != null ? m.Sender.Name + " " : "") +
                    (m.Sender.Midname ?? "")
                ).Trim(),
                null,
                m.VoiceMessage != null,
                m.Poll != null,
                m.MessageFiles.Any()
            ))
            .AsNoTracking()
            .ToListAsync(ct);

        // Одним запросом — SystemMessages
        var systemMessages = await _context.SystemMessages
            .Where(m => lastMessageIds.Contains(m.Id))
            .Select(m => new LastMessageProjection(
                m.Id,
                m.ChatId,
                m.CreatedAt,
                true,
                m.InitiatorId,
                m.Content,
                m.SystemEventType,
                m.TargetUserId,
                m.Initiator == null ? null : (
                    (m.Initiator.Surname != null ? m.Initiator.Surname + " " : "") +
                    (m.Initiator.Name != null ? m.Initiator.Name + " " : "") +
                    (m.Initiator.Midname ?? "")
                ).Trim(),
                m.TargetUser == null ? null : (
                    (m.TargetUser.Surname != null ? m.TargetUser.Surname + " " : "") +
                    (m.TargetUser.Name != null ? m.TargetUser.Name + " " : "") +
                    (m.TargetUser.Midname ?? "")
                ).Trim(),
                false,
                false,
                false
            ))
            .AsNoTracking()
            .ToListAsync(ct);

        return [.. userMessages, .. systemMessages];
    }

    public async Task<List<DialogPartnerProjection>> GetDialogPartnersAsync(IEnumerable<int> chatIds, int currentUserId, CancellationToken ct = default)
    {
        var ids = chatIds.ToList();
        if (ids.Count == 0) return [];

        return await _context.ChatMembers.Where(cm => ids.Contains(cm.ChatId) && cm.UserId != currentUserId).Select(cm => new DialogPartnerProjection(
                cm.ChatId,
                cm.User.Id,
                cm.User.Surname,
                cm.User.Name,
                cm.User.Midname,
                cm.User.Avatar,
                cm.User.StatusType,
                cm.User.StatusExpiresAt)).AsNoTracking().ToListAsync(ct);
    }

    public async Task UpdateLastMessageTimeAsync(int chatId, DateTime time, CancellationToken ct = default)
         => await _context.Chats.Where(c => c.Id == chatId).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastMessageTime, time), ct);

    public async Task<List<ChatMemberProjection>> GetMembersWithUsersAsync(int chatId, CancellationToken ct = default)
        => await _context.ChatMembers.Where(cm => cm.ChatId == chatId).Select(cm => new ChatMemberProjection(
            cm.UserId,
            cm.Role,
            cm.User.Username,
            cm.User.Surname,
            cm.User.Name,
            cm.User.Midname,
            cm.User.Avatar,
            cm.User.LastOnline,
            cm.User.StatusType,
            cm.User.StatusExpiresAt)).AsNoTracking().ToListAsync(ct);

    public async Task<List<string>> GetVoiceFilePathsAsync(int chatId, CancellationToken ct = default)
        => await _context.VoiceMessages.Where(v => _context.UserMessages.Any(m => m.Id == v.MessageId && m.ChatId == chatId))
                                       .Select(v => v.FilePath).ToListAsync(ct);

    public async Task<ChatType?> GetChatTypeAsync(int chatId, CancellationToken ct = default)
        => await _context.Chats.Where(c => c.Id == chatId).Select(c => (ChatType?)c.Type).FirstOrDefaultAsync(ct);

    public Task<bool?> GetShowHistoryForNewMembersAsync(int chatId, CancellationToken ct = default)
        => _context.Chats.Where(c => c.Id == chatId).Select(c => (bool?)c.ShowHistoryForNewMembers).FirstOrDefaultAsync(ct);

    public Task<List<Chat>> GetContactChatsWithMembersAsync(IEnumerable<int> chatIds, CancellationToken ct = default)
        => _context.Chats.Where(c => chatIds.Contains(c.Id) && c.Type == ChatType.Contact).Include(c => c.ChatMembers)
                         .ThenInclude(cm => cm.User).AsNoTracking().ToListAsync(ct);

    public Task<List<Chat>> SearchGroupChatsAsync(IEnumerable<int> chatIds, string query, int take, CancellationToken ct = default)
        => _context.Chats.Where(c => chatIds.Contains(c.Id) && c.Type != ChatType.Contact && (string.IsNullOrEmpty(query)
                         || EF.Functions.ILike(c.Name ?? "", $"%{query}%"))).Take(take).AsNoTracking().ToListAsync(ct);

    public Task<List<MemberNotificationProjection>> GetMembersForNotificationAsync(int chatId, int? excludeUserId, CancellationToken ct = default)
        => _context.ChatMembers.Where(cm => cm.ChatId == chatId && (!excludeUserId.HasValue || cm.UserId != excludeUserId.Value))
            .Select(cm => new MemberNotificationProjection(cm.UserId, cm.User.Username, cm.NotificationsEnabled, cm.User.UserSetting == null || cm.User.UserSetting.NotificationsEnabled))
            .AsNoTracking().ToListAsync(ct);

    public async Task<Dictionary<int, DateTime>> GetHistoryRestrictionsAsync(IEnumerable<int> chatIds, int userId, CancellationToken ct = default)
    {
        var ids = chatIds.ToList();

        var hiddenChatIds = await _context.Chats.Where(c => ids.Contains(c.Id) && !c.ShowHistoryForNewMembers).Select(c => c.Id).ToListAsync(ct);

        if (hiddenChatIds.Count == 0) return [];

        var memberJoinDates = await _context.ChatMembers.Where(cm => hiddenChatIds.Contains(cm.ChatId) && cm.UserId == userId
            && cm.Role != ChatRole.Owner && cm.Role != ChatRole.Admin).Select(cm => new { cm.ChatId, cm.JoinedAt }).ToListAsync(ct);

        return memberJoinDates.ToDictionary(m => m.ChatId, m => m.JoinedAt);
    }
}