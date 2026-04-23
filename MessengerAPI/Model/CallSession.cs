namespace MessengerAPI.Model;

public class CallSession
{
    public string CallId { get; init; } = Guid.NewGuid().ToString();
    public int ChatId { get; init; }
    public int InitiatorId { get; init; }
    public DateTime StartedAt { get; init; }
    public CallStatus Status { get; set; } = CallStatus.Ringing;

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

public class CallParticipant
{
    public int UserId { get; init; }
    public string ConnectionId { get; set; } = string.Empty;
    public bool IsMuted { get; set; }
    public bool IsSpeaking { get; set; }
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
}