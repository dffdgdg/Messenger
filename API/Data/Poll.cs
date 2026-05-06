namespace API.Data;

public class Poll
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public bool? IsAnonymous { get; set; }
    public bool? AllowsMultipleAnswers { get; set; }
    public DateTime? ClosesAt { get; set; }
    public virtual UserMessage? Message { get; set; }
    public virtual ICollection<PollOption> PollOptions { get; set; } = [];
    public virtual ICollection<PollVote> PollVotes { get; set; } = [];
}