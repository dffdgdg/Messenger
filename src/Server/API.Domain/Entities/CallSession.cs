using Shared.Enum;
using System.Collections.Concurrent;

namespace API.Domain.Entities;

public class CallSession
{
    public string CallId { get; init; } = Guid.NewGuid().ToString();
    public int ChatId { get; init; }
    public int InitiatorId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public CallStatus Status { get; set; } = CallStatus.Ringing;
    public CallMode Mode { get; init; } = CallMode.PeerToPeer;

    /// <summary>
    /// Contact = 1:1, группа = любой может войти
    /// </summary>
    public bool IsGroupCall { get; init; }

    /// <summary>
    /// Участники которым разослан IncomingCall и ещё не ответили.
    /// Только для Contact — в группе не используется.
    /// </summary>
    public ConcurrentDictionary<int, CallParticipant> PendingParticipants { get; } = new();

    /// <summary>Участники принявшие звонок</summary>
    public ConcurrentDictionary<int, CallParticipant> ActiveParticipants { get; } = new();

    /// <summary>
    /// CTS для таймаута — только для Contact.
    /// Отменяется когда кто-то принимает или звонок завершается.
    /// </summary>
    public CancellationTokenSource TimeoutCts { get; } = new();
}