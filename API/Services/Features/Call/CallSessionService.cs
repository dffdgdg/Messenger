namespace API.Services.Call;

public partial class CallSessionService(ILogger<CallSessionService> logger) : ICallSessionService
{
    private readonly ConcurrentDictionary<string, CallSession> _calls = new();

    private readonly ConcurrentDictionary<int, string> _chatCallIndex = new();

    public Task<CallSession?> CreateCallAsync(int chatId, int initiatorId, string initiatorConnectionId, bool isGroupCall)
    {
        if (_chatCallIndex.ContainsKey(chatId))
        {
            LogCallAlreadyExists(chatId);
            return Task.FromResult<CallSession?>(null);
        }

        var session = new CallSession
        {
            ChatId = chatId,
            InitiatorId = initiatorId,
            StartedAt = DateTime.UtcNow,
            Status = CallStatus.Ringing,
            IsGroupCall = isGroupCall
        };

        var initiator = new CallParticipant
        {
            UserId = initiatorId,
            ConnectionId = initiatorConnectionId,
            JoinedAt = DateTime.UtcNow
        };

        session.ActiveParticipants[initiatorId] = initiator;

        if (!_calls.TryAdd(session.CallId, session))
        {
            return Task.FromResult<CallSession?>(null);
        }

        _chatCallIndex[chatId] = session.CallId;

        LogCallCreated(session.CallId, chatId, initiatorId);

        return Task.FromResult<CallSession?>(session);
    }

    public CallSession? GetCall(string callId)
        => _calls.TryGetValue(callId, out var session) ? session : null;

    public IEnumerable<CallSession> GetAllSessionsForUser(int userId) =>
        _calls.Values.Where(s => s.ActiveParticipants.ContainsKey(userId) || s.PendingParticipants.ContainsKey(userId));

    public CallSession? GetActiveCallInChat(int chatId)
    {
        if (!_chatCallIndex.TryGetValue(chatId, out var callId))
            return null;

        return GetCall(callId);
    }

    public bool JoinCall(string callId, int userId, string connectionId)
    {
        if (!_calls.TryGetValue(callId, out var session))
            return false;

        if (session.Status == CallStatus.Ended)
            return false;

        var participant = new CallParticipant
        {
            UserId = userId,
            ConnectionId = connectionId,
            JoinedAt = DateTime.UtcNow
        };

        session.PendingParticipants.TryRemove(userId, out _);
        session.ActiveParticipants[userId] = participant;

        if (session.Status == CallStatus.Ringing)
        {
            session.Status = CallStatus.Active;
            session.TimeoutCts.Cancel();
        }

        LogUserJoined(userId, callId);

        return true;
    }

    public bool LeaveCall(string callId, int userId, out bool shouldEnd)
    {
        shouldEnd = false;

        if (!_calls.TryGetValue(callId, out var session))
            return false;

        session.ActiveParticipants.TryRemove(userId, out _);
        session.PendingParticipants.TryRemove(userId, out _);

        LogUserLeft(userId, callId, session.ActiveParticipants.Count);

        shouldEnd = session.ActiveParticipants.IsEmpty;
        return true;
    }

    public void UpdateConnectionId(string callId, int userId, string newConnectionId)
    {
        if (!_calls.TryGetValue(callId, out var session))
            return;

        if (session.ActiveParticipants.TryGetValue(userId, out var p))
            p.ConnectionId = newConnectionId;
    }

    public bool SetMuted(string callId, int userId, bool isMuted)
    {
        if (!_calls.TryGetValue(callId, out var session))
            return false;

        if (!session.ActiveParticipants.TryGetValue(userId, out var p))
            return false;

        p.IsMuted = isMuted;
        return true;
    }

    public bool SetSpeaking(string callId, int userId, bool isSpeaking)
    {
        if (!_calls.TryGetValue(callId, out var session))
            return false;

        if (!session.ActiveParticipants.TryGetValue(userId, out var p))
            return false;

        p.IsSpeaking = isSpeaking;
        return true;
    }

    public Task<TimeSpan> EndCallAsync(string callId)
    {
        if (!_calls.TryRemove(callId, out var session))
            return Task.FromResult(TimeSpan.Zero);

        _chatCallIndex.TryRemove(session.ChatId, out _);

        session.TimeoutCts.Cancel();
        session.Status = CallStatus.Ended;

        var duration = DateTimeOffset.UtcNow - session.StartedAt;

        LogCallEnded(callId, duration);

        return Task.FromResult(duration);
    }

    public CallStateDto ToStateDto(CallSession session, Func<int, string?> avatarResolver, Func<int, string?> nameResolver) => new()
    {
        CallId = session.CallId,
        ChatId = session.ChatId,
        Status = session.Status,
        InitiatorId = session.InitiatorId,
        StartedAt = session.StartedAt,
        IsGroupCall = session.IsGroupCall,
        ElapsedSeconds = (int)(DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds, // <-- сервер считает
        Participants = [.. session.ActiveParticipants.Values.Select(p =>
    {
        var name = nameResolver(p.UserId);
        var avatar = avatarResolver(p.UserId);
        logger.LogInformation("[CallSessionService] ToStateDto: userId={UserId} name={Name} avatar={Avatar}",
            p.UserId, name ?? "NULL", avatar ?? "NULL");
        return new CallParticipantDto
        {
            UserId = p.UserId,
            IsMuted = p.IsMuted,
            IsSpeaking = p.IsSpeaking,
            DisplayName = name ?? $"User {p.UserId}",
            AvatarUrl = avatar
        };
    })]
    };

    #region Log
    [LoggerMessage(Level = LogLevel.Warning, Message = "Попытка создать звонок в чате {ChatId}, где уже есть активный звонок")]
    private partial void LogCallAlreadyExists(int chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Создан звонок {CallId} в чате {ChatId} инициатором {UserId}")]
    private partial void LogCallCreated(string callId, int chatId, int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} присоединился к звонку {CallId}")]
    private partial void LogUserJoined(int userId, string callId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} покинул звонок {CallId}. Активных: {Count}")]
    private partial void LogUserLeft(int userId, string callId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Звонок {CallId} завершён. Длительность: {Duration}")]
    private partial void LogCallEnded(string callId, TimeSpan duration);
    #endregion
}