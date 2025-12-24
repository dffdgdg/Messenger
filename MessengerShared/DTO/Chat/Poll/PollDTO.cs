namespace MessengerShared.DTO
{
    public class PollDTO
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public int ChatId { get; set; }
        public int CreatedById { get; set; }
        public string Question { get; set; } = string.Empty;
        public bool? IsAnonymous { get; set; }
        public bool? AllowsMultipleAnswers { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosesAt { get; set; }
        public List<PollOptionDTO> Options { get; set; } = [];
        public List<int>? SelectedOptionIds { get; set; }
        public bool CanVote { get; set; }
    }

    public class PollOptionDTO
    {
        public int Id { get; set; }
        public int PollId { get; set; }
        public string OptionText { get; set; } = string.Empty;
        public string Text
        {
            get => OptionText;
            set => OptionText = value;
        }
        public int Position { get; set; }
        public List<PollVoteDTO> Votes { get; set; } = [];
        public int VotesCount { get; set; }
    }

    public class PollVoteDTO
    {
        public int PollId { get; set; }
        public int UserId { get; set; }
        public int? OptionId { get; set; }
        public List<int>? OptionIds { get; set; }
    }
}