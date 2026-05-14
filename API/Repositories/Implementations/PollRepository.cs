using API.Repositories.Abstarctions;

namespace API.Repositories.Implementations;

public sealed class PollRepository(MessengerDbContext context) : IPollRepository
{
    public void Add(Poll poll) => context.Polls.Add(poll);
    public void AddOption(PollOption option) => context.PollOptions.Add(option);
    public void AddVote(PollVote vote) => context.PollVotes.Add(vote);
    public void RemoveVotes(IEnumerable<PollVote> votes) => context.PollVotes.RemoveRange(votes);
    public Task<Poll?> FindByIdWithDetailsAsync(int pollId, CancellationToken ct = default)
        => context.Polls.Include(p => p.PollOptions).ThenInclude(o => o.PollVotes).Include(p => p.Message).FirstOrDefaultAsync(p => p.Id == pollId, ct);
    public Task<Poll?> FindByMessageIdAsync(int messageId, CancellationToken ct = default)
        => context.Polls.Include(p => p.PollOptions).ThenInclude(o => o.PollVotes).FirstOrDefaultAsync(p => p.MessageId == messageId, ct);
    public Task<List<PollVote>> GetUserVotesAsync(int pollId, int userId, CancellationToken ct = default)
        => context.PollVotes.Where(v => v.PollId == pollId && v.UserId == userId).ToListAsync(ct);
    public Task<int> CloseAsync(int pollId, DateTime closedAt, CancellationToken ct = default)
        => context.Polls.Where(p => p.Id == pollId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ClosesAt, closedAt), ct);
}