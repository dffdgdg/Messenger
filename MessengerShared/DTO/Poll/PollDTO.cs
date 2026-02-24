namespace MessengerShared.Dto.Poll
{
    public class CreatePollOptionDto
    {
        public string Text { get; set; } = string.Empty;
        public int Position { get; set; }
    }

    /// <summary>
    /// Опрос для отображения.
    /// </summary>
    public class PollDto
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public bool IsAnonymous { get; set; }
        public bool AllowsMultipleAnswers { get; set; }
        public DateTime? ClosesAt { get; set; }
        public List<PollOptionDto> Options { get; set; } = [];
        public List<int> SelectedOptionIds { get; set; } = [];
        public bool CanVote { get; set; }
    }
}