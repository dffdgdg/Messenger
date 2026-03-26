namespace MessengerShared.Dto.Poll;

public class CreatePollDto
{
    public int ChatId { get; set; }
    public string Question { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public bool AllowsMultipleAnswers { get; set; }
    public DateTime? ClosesAt { get; set; }
    public List<CreatePollOptionDto> Options { get; set; } = [];
}