namespace MessengerShared.DTO
{
    public class PagedMessagesDTO
    {
        public List<MessageDTO> Messages { get; set; } = [];
        public int TotalCount { get; set; }
        public bool HasMoreMessages { get; set; }
        public bool HasNewerMessages { get; set; }
        public int CurrentPage { get; set; }
    }
}