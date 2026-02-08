namespace MessengerShared.DTO.Chat.Poll
{
    /// <summary>
    /// «апрос на создание опроса.
    /// Question здесь Ч потому что клиент передаЄт вопрос,
    /// сервер запишет его в Message.Content.
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

    public class PollOptionDTO
    {
        public int Id { get; set; }
        public int PollId { get; set; }
        public string Text { get; set; } = string.Empty;
        public int Position { get; set; }
        public int VotesCount { get; set; }
        public List<PollVoteDTO> Votes { get; set; } = [];
    }

    public class PollVoteDTO
    {
        public int PollId { get; set; }
        public int UserId { get; set; }
        public int? OptionId { get; set; }
        public List<int>? OptionIds { get; set; }
    }
}