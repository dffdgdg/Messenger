namespace MessengerShared.Dto.Poll;

public class PollOptionDto
{
    public int Id { get; set; }
    public int PollId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Position { get; set; }
    public int VotesCount { get; set; }
    public List<PollVoteDto> Votes { get; set; } = [];
}