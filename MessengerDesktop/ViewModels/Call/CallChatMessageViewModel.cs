using MessengerShared.DTO.Call;

namespace MessengerDesktop.ViewModels.Call;

public class CallChatMessageViewModel(CallChatMessageDto dto, int myUserId)
{
    public int SenderId { get; } = dto.SenderId;
    public string SenderName { get; } = dto.SenderName;
    public string? SenderAvatar { get; } = dto.SenderAvatar;
    public string Text { get; } = dto.Text;
    public string TimeText { get; } = dto.SentAt.ToLocalTime().ToString("HH:mm");
    public bool IsOwn { get; } = dto.SenderId == myUserId;
}