namespace MessengerShared.DTO.Poll;
/// <summary>
/// Запрос на создание опроса.
/// </summary>
public class CreatePollDTO
{
    public int ChatId { get; set; }
    public string Question { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public bool AllowsMultipleAnswers { get; set; }
    public DateTime? ClosesAt { get; set; }
    public List<CreatePollOptionDTO> Options { get; set; } = [];
}