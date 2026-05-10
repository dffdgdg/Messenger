using Shared.DTO.Call;
using Shared.Hubs;
using System.Security.Claims;

namespace API.Hubs;

[Authorize]
public partial class CallHub(ICallSessionService callSessions, IAccessControlService accessControl, ISystemMessageService systemMessages,
    MessengerDbContext db, ILogger<CallHub> logger) : Hub
{
    private const int MaxParticipants = 12;
    private const int RingingTimeoutSeconds = 60;

    private readonly ConcurrentDictionary<int, Task<(string? Name, string? Avatar)>> _userCache = new();

    private Task<(string? Name, string? Avatar)> GetUserInfoAsync(int userId)
        => _userCache.GetOrAdd(userId, FetchUserInfoAsync);

    private async Task<(string? Name, string? Avatar)> FetchUserInfoAsync(int userId)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.Avatar })
            .FirstOrDefaultAsync();

        return user != null ? (user.DisplayName, user.Avatar) : (null, null);
    }

    private async Task<string> GetChatNameAsync(int chatId)
        => await db.Chats.AsNoTracking().Where(c => c.Id == chatId).Select(c => c.Name).FirstOrDefaultAsync() ?? string.Empty;

    private async Task<CallStateDto> ToStateDtoAsync(CallSession session)
    {
        var userIds = session.ActiveParticipants.Keys.Append(session.InitiatorId).Distinct();

        await Task.WhenAll(userIds.Select(GetUserInfoAsync));

        return callSessions.ToStateDto(session,
            userId => _userCache.TryGetValue(userId, out var t) && t.IsCompletedSuccessfully ? t.Result.Avatar : null,
            userId => _userCache.TryGetValue(userId, out var t) && t.IsCompletedSuccessfully ? t.Result.Name : null);
    }

    private int CurrentUserId
    {
        get
        {
            var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (int.TryParse(raw, out var userId))
                return userId;

            logger.LogError("[CallHub] Не удалось получить UserId из claims. ConnectionId={ConnectionId}, Value={Value}", Context.ConnectionId, raw);

            throw new HubException("Невозможно идентифицировать пользователя.");
        }
    }

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");

        var chatIds = await accessControl.GetUserChatIdsAsync(userId);
        foreach (var chatId in chatIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");

        await base.OnConnectedAsync();
    }

    public async Task InitiateCall(int chatId)
    {
        var userId = CurrentUserId;

        if (!await accessControl.IsMemberAsync(userId, chatId))
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallError, "Нет доступа к чату");
            return;
        }

        if (callSessions.GetActiveCallInChat(chatId) != null)
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallError, "В этом чате уже идёт звонок");
            return;
        }

        var chatType = await accessControl.GetChatTypeAsync(chatId);
        var isGroup = chatType != ChatType.Contact;

        var session = await callSessions.CreateCallAsync(chatId, userId, Context.ConnectionId, isGroup);

        if (session == null)
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallError, "Не удалось создать звонок");
            return;
        }

        var memberIds = await accessControl.GetChatMemberIdsAsync(chatId);
        var (name, avatar) = await GetUserInfoAsync(userId);

        var invite = new CallInviteDto
        {
            CallId = session.CallId,
            ChatId = chatId,
            ChatName = await GetChatNameAsync(chatId),
            InitiatorId = userId,
            InitiatorName = name ?? $"User {userId}",
            InitiatorAvatar = avatar,
            ActiveParticipantsCount = 1,
            IsGroupCall = isGroup
        };

        foreach (var memberId in memberIds.Where(id => id != userId))
        {
            if (!isGroup)
            {
                session.PendingParticipants[memberId] = new CallParticipant { UserId = memberId };
            }

            await Clients.Group($"user_{memberId}").SendAsync(HubMethods.Call.IncomingCall, invite);
        }

        var stateDto = await ToStateDtoAsync(session);
        await Clients.Caller.SendAsync(HubMethods.Call.CallStateUpdated, stateDto);
        await Clients.Group($"chat_{chatId}").SendAsync(HubMethods.Call.ActiveCallStarted, stateDto);

        await systemMessages.CreateCallStartedMessageAsync(chatId, userId);

        if (!isGroup)
        {
            _ = StartRingingTimeoutAsync(session.CallId, chatId, RingingTimeoutSeconds);
        }
    }

    public async Task JoinCall(string callId)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);

        if (session == null)
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallEnded, callId, CallEndReason.Ended);
            return;
        }

        if (session.ActiveParticipants.Count >= MaxParticipants)
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallError, $"Максимум {MaxParticipants} участников");
            return;
        }

        if (!await accessControl.IsMemberAsync(userId, session.ChatId))
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallError, "Нет доступа к чату");
            return;
        }

        var joined = callSessions.JoinCall(callId, userId, Context.ConnectionId);
        if (!joined)
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallEnded, callId, CallEndReason.Ended);
            return;
        }

        var stateDto = await ToStateDtoAsync(session);
        await Clients.Caller.SendAsync(HubMethods.Call.CallStateUpdated, stateDto);

        var (name, avatar) = await GetUserInfoAsync(userId);
        var notification = new CallParticipantDto
        {
            UserId = userId,
            DisplayName = name ?? $"User {userId}",
            AvatarUrl = avatar
        };

        foreach (var participant in session.ActiveParticipants.Values.Where(p => p.UserId != userId))
        {
            await Clients.Client(participant.ConnectionId).SendAsync(HubMethods.Call.CallParticipantJoined, callId, notification);
        }
    }

    public async Task LeaveCall(string callId)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        var chatId = session.ChatId;
        var isGroup = session.IsGroupCall;

        callSessions.LeaveCall(callId, userId, out var shouldEnd);

        foreach (var p in session.ActiveParticipants.Values)
        {
            await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.CallParticipantLeft, callId, userId);
        }

        if (shouldEnd)
        {
            await TerminateCallAsync(callId, chatId, CallEndReason.Ended);
        }
        else if (isGroup)
        {
            await Clients.Group($"chat_{chatId}").SendAsync(HubMethods.Call.ActiveCallUpdated, await ToStateDtoAsync(session));
        }
    }

    public async Task DeclineCall(string callId)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);

        if (session == null)
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallEnded, callId, CallEndReason.Ended);
            return;
        }

        if (session.IsGroupCall) return;

        session.PendingParticipants.TryRemove(userId, out _);

        if (session.Status == CallStatus.Ringing && session.PendingParticipants.IsEmpty)
        {
            await TerminateCallAsync(callId, session.ChatId, CallEndReason.Declined);
            return;
        }

        await Clients.Caller.SendAsync(HubMethods.Call.CallEnded, callId, CallEndReason.Declined);
    }

    public async Task CancelCall(string callId)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        if (session.InitiatorId != userId)
        {
            await Clients.Caller.SendAsync(HubMethods.Call.CallError, "Только инициатор может отменить звонок");
            return;
        }

        if (session.IsGroupCall || session.Status != CallStatus.Ringing)
        {
            await LeaveCall(callId);
            return;
        }

        await TerminateCallAsync(callId, session.ChatId, CallEndReason.Cancelled);
    }

    public async Task SendSignal(WebRtcSignalDto signal)
    {
        var session = callSessions.GetCall(signal.CallId);
        if (session == null) return;

        signal.FromUserId = CurrentUserId;

        if (!session.ActiveParticipants.TryGetValue(signal.TargetUserId, out var target))
            return;

        await Clients.Client(target.ConnectionId).SendAsync(HubMethods.Call.ReceiveSignal, signal);
    }

    public async Task ToggleMute(string callId, bool isMuted)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        callSessions.SetMuted(callId, userId, isMuted);

        foreach (var p in session.ActiveParticipants.Values.Where(p => p.UserId != userId))
        {
            await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.ParticipantMuteChanged, callId, userId, isMuted);
        }
    }

    public async Task ToggleSpeaking(string callId, bool isSpeaking)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        if (!session.ActiveParticipants.ContainsKey(userId)) return;

        callSessions.SetSpeaking(callId, userId, isSpeaking);

        var others = session.ActiveParticipants.Values.Where(p => p.UserId != userId).Select(p => p.ConnectionId).Where(c => !string.IsNullOrEmpty(c)).ToList();

        if (others.Count == 0) return;

        await Clients.Clients(others).SendAsync(HubMethods.Call.ParticipantSpeakingChanged, callId, userId, isSpeaking);
    }

    public async Task<CallStateDto?> GetCallState(int chatId)
    {
        var session = callSessions.GetActiveCallInChat(chatId);
        if (session == null) return null;

        if (!await accessControl.IsMemberAsync(CurrentUserId, chatId))
            return null;

        return await ToStateDtoAsync(session);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = CurrentUserId;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");

        foreach (var session in FindSessionsForUser(userId))
        {
            callSessions.LeaveCall(session.CallId, userId, out var shouldEnd);

            foreach (var p in session.ActiveParticipants.Values)
            {
                await Clients.Client(p.ConnectionId)
                    .SendAsync(HubMethods.Call.CallParticipantLeft, session.CallId, userId);
            }

            if (shouldEnd)
                await TerminateCallAsync(session.CallId, session.ChatId, CallEndReason.Ended);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task TerminateCallAsync(string callId, int chatId, CallEndReason reason)
    {
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        var initiatorId = session.InitiatorId;

        foreach (var pending in session.PendingParticipants.Values)
        {
            await Clients.Group($"user_{pending.UserId}").SendAsync(HubMethods.Call.CallEnded, callId, reason);
        }

        foreach (var active in session.ActiveParticipants.Values)
        {
            await Clients.Client(active.ConnectionId).SendAsync(HubMethods.Call.CallEnded, callId, reason);
        }

        await Clients.Group($"chat_{chatId}").SendAsync(HubMethods.Call.ActiveCallEnded, callId);

        var duration = await callSessions.EndCallAsync(callId);

        if (reason != CallEndReason.Cancelled && reason != CallEndReason.Declined && duration > TimeSpan.FromSeconds(1))
        {
            await systemMessages.CreateCallEndedMessageAsync(chatId, initiatorId, duration);
        }
    }

    public async Task SendCallMessage(string callId, string text)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        if (!session.ActiveParticipants.ContainsKey(userId)) return;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) return;

        var (name, avatar) = await GetUserInfoAsync(userId);

        var message = new CallChatMessageDto
        {
            CallId = callId,
            SenderId = userId,
            SenderName = name ?? $"User {userId}",
            SenderAvatar = avatar,
            Text = text.Trim(),
            SentAt = DateTime.UtcNow
        };

        var connectionIds = session.ActiveParticipants.Values.Select(p => p.ConnectionId).Where(c => !string.IsNullOrEmpty(c)).ToList();

        await Clients.Clients(connectionIds).SendAsync(HubMethods.Call.CallMessageReceived, message);
    }

    private async Task StartRingingTimeoutAsync(string callId, int chatId, int timeoutSeconds)
    {
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), session.TimeoutCts.Token);

            if (session.Status == CallStatus.Ringing)
            {
                LogCallTimedOut(callId, chatId);
                await TerminateCallAsync(callId, chatId, CallEndReason.Timeout);
            }
        }
        catch (OperationCanceledException) { /* Кто-то принял или звонок завершён */ }
    }

    private IEnumerable<CallSession> FindSessionsForUser(int userId)
        => callSessions.GetAllSessionsForUser(userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Звонок {CallId} в чате {ChatId} завершён по таймауту")]
    private partial void LogCallTimedOut(string callId, int chatId);
}