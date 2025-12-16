namespace MessengerShared.DTO
{
    public class SearchMessagesRequestDTO
    {
        public int ChatId { get; set; }
        public string Query { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class SearchMessagesResponseDTO
    {
        public List<MessageDTO> Messages { get; set; } = [];
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; }
        public bool HasMoreMessages { get; set; }
    }
}
