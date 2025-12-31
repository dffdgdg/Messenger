namespace MessengerAPI.Model;

public class PollVote
{
    public int Id { get; set; }

    public int PollId { get; set; }

    public int OptionId { get; set; }

    public int UserId { get; set; }

    public DateTime VotedAt { get; set; }

    public virtual PollOption Option { get; set; } = null!;

    public virtual Poll Poll { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
