using MessengerShared.DTO.Message;

namespace MessengerShared.DTO.Search
{
    public class SearchMessagesResponseDTO
    {
        public List<MessageDTO> Messages { get; set; } = [];
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; }
        public bool HasMoreMessages { get; set; }
    }
}