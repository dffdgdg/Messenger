namespace Shared.Contracts.Poll;

public class PollVoteDto
{
    public int PollId { get; set; }
    public int UserId { get; set; }
    public int? OptionId { get; set; }
    public List<int>? OptionIds { get; set; }
}