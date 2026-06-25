namespace API.Domain.Entities;

public class Poll
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public bool IsAnonymous { get; set; } = true;
    public bool AllowsMultipleAnswers { get; set; } = false;
    public DateTime? ClosesAt { get; set; }
    public virtual UserMessage? Message { get; set; }
    public virtual ICollection<PollOption> PollOptions { get; set; } = [];
    public virtual ICollection<PollVote> PollVotes { get; set; } = [];
}