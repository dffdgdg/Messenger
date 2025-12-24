namespace MessengerShared.DTO
{
    public class MarkAsReadDTO
    {
        public int ChatId { get; set; }
        public int? MessageId { get; set; }
    }

    public class ReadReceiptResponseDTO
    {
        public int ChatId { get; set; }
        public int? LastReadMessageId { get; set; }
        public DateTime? LastReadAt { get; set; }
        public int UnreadCount { get; set; }
    }

    public record UnreadCountDTO(int ChatId, int UnreadCount);

    public class AllUnreadCountsDTO
    {
        public List<UnreadCountDTO> Chats { get; set; } = [];
        public int TotalUnread { get; set; }
    }

    /// <summary>
    /// Информация о прочтении для пользователя в конкретном чате
    /// </summary>
    public class ChatReadInfoDTO
    {
        public int ChatId { get; set; }
        public int? LastReadMessageId { get; set; }
        public DateTime? LastReadAt { get; set; }
        public int UnreadCount { get; set; }
        public int? FirstUnreadMessageId { get; set; }
    }
}