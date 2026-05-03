namespace API.Data;

public class CallParticipant
{
    public int UserId { get; init; }
    public string ConnectionId { get; set; } = string.Empty;
    public bool IsMuted { get; set; }
    public bool IsSpeaking { get; set; }
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
}