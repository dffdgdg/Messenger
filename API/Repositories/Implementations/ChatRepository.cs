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

    public async Task<List<Chat>> GetByIdsLightAsync(IEnumerable<int> chatIds, CancellationToken ct = default)
        => await _context.Chats.Where(c => chatIds.Contains(c.Id)).AsNoTracking().ToListAsync(ct);

    public async Task<List<LastMessageProjection>> GetLastMessagesAsync(IEnumerable<int> chatIds, CancellationToken ct = default)
    {
        var ids = chatIds.ToList();

        var lastMessageIds = await _context.Messages.Where(m => ids.Contains(m.ChatId) && m.IsDeleted != true).GroupBy(m => m.ChatId)
                                                    .Select(g => g.Max(m => m.Id)).ToListAsync(ct);

        if (lastMessageIds.Count == 0)
            return [];

        return await _context.Messages.Where(m => lastMessageIds.Contains(m.Id)).Select(m => new LastMessageProjection(m.Id, m.ChatId,
                m.CreatedAt,
                m is SystemMessage,
                m is UserMessage
                    ? ((UserMessage)(object)m).SenderId
                    : ((SystemMessage)(object)m).InitiatorId,
                m is UserMessage
                    ? ((UserMessage)(object)m).Content
                    : ((SystemMessage)(object)m).Content,
                m is SystemMessage
                    ? ((SystemMessage)(object)m).SystemEventType
                    : null,
                m is SystemMessage
                    ? ((SystemMessage)(object)m).TargetUserId
                    : null,
                m is UserMessage
                    ? FormatName(
                        ((UserMessage)(object)m).Sender!.Surname,
                        ((UserMessage)(object)m).Sender!.Name,
                        ((UserMessage)(object)m).Sender!.Midname)
                    : FormatName(
                        ((SystemMessage)(object)m).Initiator!.Surname,
                        ((SystemMessage)(object)m).Initiator!.Name,
                        ((SystemMessage)(object)m).Initiator!.Midname),
                m is SystemMessage
                    ? FormatName(
                        ((SystemMessage)(object)m).TargetUser!.Surname,
                        ((SystemMessage)(object)m).TargetUser!.Name,
                        ((SystemMessage)(object)m).TargetUser!.Midname)
                    : null,
                m is UserMessage && ((UserMessage)(object)m).VoiceMessage != null,
                m is UserMessage && ((UserMessage)(object)m).Poll != null,
                m is UserMessage && ((UserMessage)(object)m).MessageFiles.Any()
            )).AsNoTracking().ToListAsync(ct);
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

    private static string FormatName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(" ", parts);
    }
    // ChatRepository.cs

    public Task<bool?> GetShowHistoryForNewMembersAsync(
        int chatId, CancellationToken ct = default)
        => _context.Chats
            .Where(c => c.Id == chatId)
            .Select(c => (bool?)c.ShowHistoryForNewMembers)
            .FirstOrDefaultAsync(ct);

    public Task<List<Chat>> GetContactChatsWithMembersAsync(
        IEnumerable<int> chatIds, CancellationToken ct = default)
        => _context.Chats
            .Where(c => chatIds.Contains(c.Id) && c.Type == ChatType.Contact)
            .Include(c => c.ChatMembers)
                .ThenInclude(cm => cm.User)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<List<Chat>> SearchGroupChatsAsync(
        IEnumerable<int> chatIds, string query, int take,
        CancellationToken ct = default)
        => _context.Chats
            .Where(c => chatIds.Contains(c.Id)
                     && c.Type != ChatType.Contact
                     && (string.IsNullOrEmpty(query)
                         || EF.Functions.ILike(c.Name ?? "", $"%{query}%")))
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<List<MemberNotificationProjection>> GetMembersForNotificationAsync(
        int chatId, int? excludeUserId, CancellationToken ct = default)
        => _context.ChatMembers
            .Where(cm => cm.ChatId == chatId
                      && (!excludeUserId.HasValue || cm.UserId != excludeUserId.Value))
            .Select(cm => new MemberNotificationProjection(
                cm.UserId,
                cm.User.Username,
                cm.NotificationsEnabled,
                cm.User.UserSetting == null || cm.User.UserSetting.NotificationsEnabled))
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<Dictionary<int, DateTime>> GetHistoryRestrictionsAsync(
        IEnumerable<int> chatIds, int userId, CancellationToken ct = default)
    {
        var ids = chatIds.ToList();

        var hiddenChatIds = await _context.Chats
            .Where(c => ids.Contains(c.Id) && !c.ShowHistoryForNewMembers)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (hiddenChatIds.Count == 0) return [];

        var memberJoinDates = await _context.ChatMembers
            .Where(cm => hiddenChatIds.Contains(cm.ChatId)
                      && cm.UserId == userId
                      && cm.Role != ChatRole.Owner
                      && cm.Role != ChatRole.Admin)
            .Select(cm => new { cm.ChatId, cm.JoinedAt })
            .ToListAsync(ct);

        return memberJoinDates.ToDictionary(m => m.ChatId, m => m.JoinedAt);
    }
}