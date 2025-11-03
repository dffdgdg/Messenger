namespace MessengerShared.DTO
{
    public class PollDTO
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public int CreatedById { get; set; }
        public int ChatId { get; set; }
        public string Question { get; set; } = string.Empty;
        public bool? IsAnonymous { get; set; }
        public bool? AllowsMultipleAnswers { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosesAt { get; set; }
        public List<PollOptionDTO> Options { get; set; } = [];
        // Selected option ids for the current user (if any)
        public List<int>? SelectedOptionIds { get; set; }
        // Helper: whether user can vote (computed on server)
        public bool CanVote { get; set; }
    }

    public class PollOptionDTO
    {
        public int Id { get; set; }
        public int PollId { get; set; }
        // Some parts of the code expect 'OptionText'
        public string OptionText { get; set; } = string.Empty;
        // Other parts expect 'Text'
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
        // Supports both single-option vote and multi-option vote payloads
        public int PollId { get; set; }
        public int UserId { get; set; }
        // Single option id (used in some endpoints)
        public int? OptionId { get; set; }
        // Multiple option ids (used by UI when selecting multiple)
        public List<int>? OptionIds { get; set; }
    }
}