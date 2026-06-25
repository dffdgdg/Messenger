using API.Application.Features.Call;
using API.Application.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Call;
using Shared.Enum;
using System.Collections.Concurrent;

namespace API.Infrastructure.Services.Features.Call;

public sealed partial class CallSessionService : ICallSessionService, IDisposable
{
    private static readonly TimeSpan StaleRingingTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StaleActiveTimeout = TimeSpan.FromHours(4);

    private readonly ConcurrentDictionary<string, CallSession> _calls = new();
    private readonly ConcurrentDictionary<int, string> _chatCallIndex = new();
    private readonly CallRelayService _relay;
    private readonly ILogger<CallSessionService> _logger;
    private readonly Timer _staleSessionTimer;
    private volatile bool _disposed;

    public CallSessionService(ILogger<CallSessionService> logger, CallRelayService relay)
    {
        _logger = logger;
        _relay = relay;
        _staleSessionTimer = new Timer(_ => CleanupStaleSessions(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public Task<CallSession?> CreateCallAsync(int chatId, int initiatorId, string initiatorConnectionId, ChatType chatType)
    {
        if (_chatCallIndex.ContainsKey(chatId))
        {
            LogCallAlreadyExists(chatId);
            return Task.FromResult<CallSession?>(null);
        }

        var mode = chatType == ChatType.Contact ? CallMode.PeerToPeer : CallMode.ServerMixed;

        var session = new CallSession
        {
            ChatId = chatId,
            InitiatorId = initiatorId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = CallStatus.Ringing,
            IsGroupCall = chatType != ChatType.Contact,
            Mode = mode
        };

        session.ActiveParticipants[initiatorId] = new CallParticipant
        {
            UserId = initiatorId,
            ConnectionId = initiatorConnectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };

        if (!_calls.TryAdd(session.CallId, session))
            return Task.FromResult<CallSession?>(null);

        _chatCallIndex[chatId] = session.CallId;

        if (session.Mode == CallMode.ServerMixed)
        {
            _relay.RegisterCall(session.CallId);
            _relay.AddParticipant(session.CallId, initiatorId);
        }

        LogCallCreated(session.CallId, chatId, initiatorId, mode);
        return Task.FromResult<CallSession?>(session);
    }

    public CallSession? GetCall(string callId)
        => _calls.TryGetValue(callId, out var session) ? session : null;

    public CallSession? GetActiveCallInChat(int chatId)
    {
        if (!_chatCallIndex.TryGetValue(chatId, out var callId))
            return null;

        return GetCall(callId);
    }

    public IEnumerable<CallSession> GetAllSessionsForUser(int userId)
        => _calls.Values.Where(s => s.ActiveParticipants.ContainsKey(userId) || s.PendingParticipants.ContainsKey(userId));

    public bool JoinCall(string callId, int userId, string connectionId)
    {
        if (!_calls.TryGetValue(callId, out var session))
            return false;

        if (session.Status == CallStatus.Ended)
            return false;

        session.PendingParticipants.TryRemove(userId, out _);
        session.ActiveParticipants[userId] = new CallParticipant
        {
            UserId = userId,
            ConnectionId = connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };

        if (session.Status == CallStatus.Ringing)
        {
            session.Status = CallStatus.Active;
            session.TimeoutCts.Cancel();
        }

        if (session.Mode == CallMode.ServerMixed)
            _relay.AddParticipant(callId, userId);

        LogUserJoined(userId, callId, session.ActiveParticipants.Count);
        return true;
    }

    public bool LeaveCall(string callId, int userId, out bool shouldEnd)
    {
        shouldEnd = false;

        if (!_calls.TryGetValue(callId, out var session))
            return false;

        session.ActiveParticipants.TryRemove(userId, out _);
        session.PendingParticipants.TryRemove(userId, out _);

        if (session.Mode == CallMode.ServerMixed)
            _relay.RemoveParticipant(callId, userId);

        shouldEnd = session.ActiveParticipants.IsEmpty;

        LogUserLeft(userId, callId, session.ActiveParticipants.Count);
        return true;
    }

    public void UpdateConnectionId(string callId, int userId, string newConnectionId)
    {
        if (!_calls.TryGetValue(callId, out var session))
            return;

        if (session.ActiveParticipants.TryGetValue(userId, out var participant))
            participant.ConnectionId = newConnectionId;
    }

    public bool SetMuted(string callId, int userId, bool isMuted)
    {
        if (!_calls.TryGetValue(callId, out var session))
            return false;

        if (!session.ActiveParticipants.TryGetValue(userId, out var participant))
            return false;

        participant.IsMuted = isMuted;
        return true;
    }

    public bool SetSpeaking(string callId, int userId, bool isSpeaking)
    {
        if (!_calls.TryGetValue(callId, out var session))
            return false;

        if (!session.ActiveParticipants.TryGetValue(userId, out var participant))
            return false;

        participant.IsSpeaking = isSpeaking;
        return true;
    }

    public Task<TimeSpan> EndCallAsync(string callId)
    {
        if (!_calls.TryRemove(callId, out var session))
            return Task.FromResult(TimeSpan.Zero);

        _chatCallIndex.TryRemove(session.ChatId, out _);
        session.TimeoutCts.Cancel();
        session.Status = CallStatus.Ended;

        if (session.Mode == CallMode.ServerMixed)
            _relay.RemoveCall(callId);

        var duration = DateTimeOffset.UtcNow - session.StartedAt;
        LogCallEnded(callId, session.ChatId, duration);
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
        Mode = session.Mode,
        ElapsedSeconds = (int)(DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds,
        Participants = [.. session.ActiveParticipants.Values.Select(p => new CallParticipantDto
        {
            UserId = p.UserId,
            IsMuted = p.IsMuted,
            IsSpeaking = p.IsSpeaking,
            DisplayName = nameResolver(p.UserId) ?? $"User {p.UserId}",
            AvatarUrl = avatarResolver(p.UserId)
        })]
    };

    private void CleanupStaleSessions()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = new List<string>();

        foreach (var (callId, session) in _calls)
        {
            var age = now - session.StartedAt;

            var isStale = session.Status switch
            {
                CallStatus.Ringing => age > StaleRingingTimeout,
                CallStatus.Active => session.ActiveParticipants.IsEmpty,
                CallStatus.Ended => true,
                _ => age > StaleActiveTimeout
            };

            if (isStale)
                stale.Add(callId);
        }

        foreach (var callId in stale)
        {
            LogStaleSession(callId);
            _ = EndCallAsync(callId);
        }

        if (stale.Count > 0)
            LogStaleSessionsCleanup(stale.Count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _staleSessionTimer.Dispose();

        foreach (var session in _calls.Values)
        {
            try { session.TimeoutCts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        _calls.Clear();
        _chatCallIndex.Clear();
        LogDisposed();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Попытка создать звонок в чате {ChatId} — уже есть активный звонок")]
    private partial void LogCallAlreadyExists(int chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Создан звонок {CallId} в чате {ChatId} инициатором {UserId}, режим: {Mode}")]
    private partial void LogCallCreated(string callId, int chatId, int userId, CallMode mode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} присоединился к звонку {CallId}. Активных: {Count}")]
    private partial void LogUserJoined(int userId, string callId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} покинул звонок {CallId}. Осталось: {Count}")]
    private partial void LogUserLeft(int userId, string callId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Звонок {CallId} в чате {ChatId} завершён. Длительность: {Duration}")]
    private partial void LogCallEnded(string callId, int chatId, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Обнаружена зависшая сессия {CallId} — принудительное завершение")]
    private partial void LogStaleSession(string callId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Очистка зависших сессий: завершено {Count} звонков")]
    private partial void LogStaleSessionsCleanup(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CallSessionService освобождён")]
    private partial void LogDisposed();
}