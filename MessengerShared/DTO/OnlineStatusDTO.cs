namespace MessengerShared.DTO
{
    public class OnlineStatusDTO
    {
        public int UserId { get; set; }
        public bool IsOnline { get; set; }
        public DateTime? LastOnline { get; set; }
    }

    public class OnlineUsersResponseDTO
    {
        public List<int> OnlineUserIds { get; set; } = [];
        public int TotalOnline { get; set; }
    }
}