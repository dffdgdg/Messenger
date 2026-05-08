using API.Repositories.Abstarctions;
using API.Repositories.Base;

namespace API.Repositories.Implementations;

public sealed class MessageRepository(MessengerDbContext context) : RepositoryBase<Message>(context), IMessageRepository
{
    public async Task<UserMessage?> FindUserMessageByIdAsync(int messageId, CancellationToken ct = default)
        => await _context.UserMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public async Task<UserMessage?> FindUserMessageWithIncludesAsync(int messageId, CancellationToken ct = default)
        => await _context.UserMessages.Include(m => m.Sender).Include(m => m.VoiceMessage).Include(m => m.MessageFiles).Include(m => m.Poll)
                                      .ThenInclude(p => p!.PollOptions).ThenInclude(o => o.PollVotes).Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender)
                                      .Include(m => m.ForwardedFromMessage).ThenInclude(f => f!.Sender).FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public async Task<List<UserMessage>> GetPagedAsync(int chatId, int skip, int take, CancellationToken ct = default)
        => await _context.UserMessages.Where(m => m.ChatId == chatId && m.IsDeleted != true)
                                      .Include(m => m.Sender).Include(m => m.MessageFiles)
                                      .OrderByDescending(m => m.CreatedAt).Skip(skip).Take(take).AsNoTracking().ToListAsync(ct);

    public async Task<List<UserMessage>> GetBeforeAsync(int chatId, int beforeId, int take, CancellationToken ct = default)
        => await _context.UserMessages.Where(m => m.ChatId == chatId && m.Id < beforeId && m.IsDeleted != true)
                                      .Include(m => m.Sender).Include(m => m.MessageFiles)
                                      .OrderByDescending(m => m.Id).Take(take).AsNoTracking().ToListAsync(ct);

    public async Task<List<UserMessage>> GetAfterAsync(int chatId, int afterId, int take, CancellationToken ct = default)
        => await _context.UserMessages.Where(m => m.ChatId == chatId && m.Id > afterId && m.IsDeleted != true)
                                      .Include(m => m.Sender).Include(m => m.MessageFiles)
                                      .OrderBy(m => m.Id).Take(take).AsNoTracking().ToListAsync(ct);

    public async Task<List<UserMessage>> GetAroundAsync(int chatId, int messageId, int half, CancellationToken ct = default)
    {
        var before = await GetBeforeAsync(chatId, messageId + 1, half, ct);
        var after = await GetAfterAsync(chatId, messageId - 1, half, ct);
        return [.. before.OrderBy(m => m.Id), .. after.OrderBy(m => m.Id)];
    }

    public async Task<List<UserMessage>> GetPinnedAsync(int chatId, CancellationToken ct = default)
        => await _context.UserMessages.Where(m => m.ChatId == chatId && m.PinnedAt != null && m.IsDeleted != true)
                                      .Include(m => m.Sender).OrderByDescending(m => m.PinnedAt).AsNoTracking().ToListAsync(ct);

    public async Task<int> CountAsync(int chatId, CancellationToken ct = default)
        => await _context.Messages.CountAsync(m => m.ChatId == chatId && m.IsDeleted != true, ct);
}