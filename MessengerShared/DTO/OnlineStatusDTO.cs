namespace MessengerShared.DTO
{
    public record OnlineStatusDTO(int UserId, bool IsOnline, DateTime? LastOnline);

    public class OnlineUsersResponseDTO
    {
        public List<int> OnlineUserIds { get; set; } = [];
        public int TotalOnline { get; set; }
    }
}