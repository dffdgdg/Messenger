namespace API.Repositories.Abstarctions;

public interface IPollRepository
{
    Task<Poll?> FindByIdWithDetailsAsync(int pollId, CancellationToken ct = default);
    Task<Poll?> FindByMessageIdAsync(int messageId, CancellationToken ct = default);
    void Add(Poll poll);
    void AddOption(PollOption option);
    void AddVote(PollVote vote);
    void RemoveVotes(IEnumerable<PollVote> votes);
    Task<List<PollVote>> GetUserVotesAsync(int pollId, int userId, CancellationToken ct = default);
    Task<int> CloseAsync(int pollId, DateTime closedAt, CancellationToken ct = default);
}