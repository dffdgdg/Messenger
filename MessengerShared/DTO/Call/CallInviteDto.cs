namespace MessengerShared.DTO.Call;

public class CallInviteDto
{
    public string CallId { get; set; } = string.Empty;
    public int ChatId { get; set; }
    public string ChatName { get; set; } = string.Empty;
    public int InitiatorId { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public string? InitiatorAvatar { get; set; }
    public int ActiveParticipantsCount { get; set; }
    public bool IsGroupCall { get; set; }
}