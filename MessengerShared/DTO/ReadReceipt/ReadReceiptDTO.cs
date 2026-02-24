namespace MessengerShared.Dto.ReadReceipt
{
    public class MarkAsReadDto
    {
        public int ChatId { get; set; }
        public int? MessageId { get; set; }
    }

    public class ReadReceiptResponseDto
    {
        public int ChatId { get; set; }
        public int? LastReadMessageId { get; set; }
        public DateTime? LastReadAt { get; set; }
        public int UnreadCount { get; set; }
    }

    public record UnreadCountDto(int ChatId, int UnreadCount);

    public class AllUnreadCountsDto
    {
        public List<UnreadCountDto> Chats { get; set; } = [];
        public int TotalUnread { get; set; }
    }

    /// <summary>
    /// Информация о прочтении для пользователя в конкретном чате
    /// </summary>
    public class ChatReadInfoDto
    {
        public int ChatId { get; set; }
        public int? LastReadMessageId { get; set; }
        public DateTime? LastReadAt { get; set; }
        public int UnreadCount { get; set; }
        public int? FirstUnreadMessageId { get; set; }
    }
}