namespace MessengerAPI.Model;

public class Poll
{
    public int Id { get; set; }

    public int MessageId { get; set; }

    public string Question { get; set; } = null!;

    public bool? IsAnonymous { get; set; }

    public bool? AllowsMultipleAnswers { get; set; }

    public DateTime? ClosesAt { get; set; }

    public virtual Message Message { get; set; } = null!;

    public virtual ICollection<PollOption> PollOptions { get; set; } = [];

    public virtual ICollection<PollVote> PollVotes { get; set; } = [];
}
