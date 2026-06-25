using Shared.Enum;
using System.Collections.Concurrent;

namespace API.Application.Features.Call;

public sealed class CallSession
{
    public string CallId { get; init; } = Guid.NewGuid().ToString();
    public int ChatId { get; init; }
    public int InitiatorId { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public CallStatus Status { get; set; } = CallStatus.Ringing;
    public CallMode Mode { get; init; } = CallMode.PeerToPeer;
    public bool IsGroupCall { get; init; }
    public ConcurrentDictionary<int, CallParticipant> PendingParticipants { get; } = new();
    public ConcurrentDictionary<int, CallParticipant> ActiveParticipants { get; } = new();
    public CancellationTokenSource TimeoutCts { get; } = new();
}
