namespace MessengerShared.DTO.Poll;

public class PollVoteDTO
{
    public int PollId { get; set; }
    public int UserId { get; set; }
    public int? OptionId { get; set; }
    public List<int>? OptionIds { get; set; }
}