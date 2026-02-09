namespace MessengerShared.DTO.Poll
{
    public class CreatePollOptionDTO
    {
        public string Text { get; set; } = string.Empty;
        public int Position { get; set; }
    }

    /// <summary>
    /// ќпрос дл€ отображени€.
    /// Question отсутствует Ч вопрос берЄтс€ из Message.Content.
    /// </summary>
    public class PollDTO
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public bool IsAnonymous { get; set; }
        public bool AllowsMultipleAnswers { get; set; }
        public DateTime? ClosesAt { get; set; }
        public List<PollOptionDTO> Options { get; set; } = [];
        public List<int> SelectedOptionIds { get; set; } = [];
        public bool CanVote { get; set; }
    }
}