namespace API.Application.Features.Call;

public sealed class CallParticipant
{
    public int UserId { get; init; }
    public string ConnectionId { get; set; } = string.Empty;
    public bool IsMuted { get; set; }
    public bool IsSpeaking { get; set; }
    public DateTimeOffset JoinedAt { get; init; } = DateTimeOffset.UtcNow;
}
