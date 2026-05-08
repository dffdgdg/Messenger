using API.Repositories.Abstarctions;
using API.Repositories.Base;

namespace API.Repositories.Implementations;

public sealed class ChatRepository(MessengerDbContext context)
    : RepositoryBase<Chat>(context), IChatRepository
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
}