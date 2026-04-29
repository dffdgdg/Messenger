using MessengerShared.Enum;

namespace MessengerShared.Dto.Online;

public record UserStatusDto(int UserId, bool IsOnline, DateTime? LastOnline, UserStatusType StatusType = UserStatusType.Online,
    DateTime? StatusExpiresAt = null
);

public class OnlineUsersResponseDto
{
    public List<int> OnlineUserIds { get; set; } = [];
    public int TotalOnline { get; set; }
}

public class SetStatusRequest
{
    public UserStatusType StatusType { get; set; }
    public string? Duration { get; set; }
}