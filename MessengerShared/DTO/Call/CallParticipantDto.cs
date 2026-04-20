namespace MessengerShared.DTO.Call;

public class CallParticipantDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSpeaking { get; set; }
}