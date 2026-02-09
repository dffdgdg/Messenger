namespace MessengerShared.DTO.Poll;

public class PollOptionDTO
{
    public int Id { get; set; }
    public int PollId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Position { get; set; }
    public int VotesCount { get; set; }
    public List<PollVoteDTO> Votes { get; set; } = [];
}