namespace MessengerShared.Dto.Online;

public record OnlineStatusDto(int UserId, bool IsOnline, DateTime? LastOnline);

public class OnlineUsersResponseDto
{
    public List<int> OnlineUserIds { get; set; } = [];
    public int TotalOnline { get; set; }
}