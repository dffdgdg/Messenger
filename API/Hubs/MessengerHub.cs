using API.Services.Infrastructure.Security;
using Shared.Hubs;
using System.Security.Claims;

namespace API.Hubs;

[Authorize]
public sealed partial class MessengerHub(
    IServiceScopeFactory scopeFactory,
    IOnlineUserService onlineUserService,
    ICallSessionService callSessions,
    IAccessControlService accessControl,
    ISystemMessageService systemMessages,
    MessengerDbContext db,
    AppDateTime appDateTime,
    ILogger<MessengerHub> logger) : Hub
{
    private const int MaxParticipants = 12;
    private const int RingingTimeoutSeconds = 60;

    // ─── User info cache (per-connection, живёт пока хаб жив) ────────────────
    private readonly ConcurrentDictionary<int, Task<(string? Name, string? Avatar)>> _userCache = new();

    #region Connection Lifecycle

    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            await base.OnConnectedAsync();
            return;
        }

        // Предзагружаем информацию о себе в кэш
        _ = GetUserInfoAsync(userId.Value);

        onlineUserService.UserConnected(userId.Value, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

        var chatIds = await accessControl.GetUserChatIdsAsync(userId.Value);
        await Task.WhenAll(chatIds.Select(id =>
            Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{id}")));

        using var scope = scopeFactory.CreateScope();
        var statusService = scope.ServiceProvider.GetRequiredService<IUserStatusService>();
        var statusResult = await statusService.GetStatusAsync(userId.Value);

        if (statusResult.TryUnwrap(out var statusDto, logger))
        {
            await Clients.Caller.SendAsync(HubMethods.Chat.UserStatusChanged, statusDto);
            await Clients.Others.SendAsync(HubMethods.Chat.UserStatusChanged, statusDto);
        }
        else
        {
            await Clients.Others.SendAsync(HubMethods.Chat.UserOnline, userId.Value);
        }

        LogUserConnected(userId.Value, chatIds.Count);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
        {
            onlineUserService.UserDisconnected(userId.Value, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

            if (!onlineUserService.IsOnline(userId.Value))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var ctx = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
                    await ctx.Users
                        .Where(u => u.Id == userId.Value)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastOnline, appDateTime.UtcNow));

                    await Clients.Others.SendAsync(HubMethods.Chat.UserOffline, userId.Value);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при обработке отключения {UserId}", userId.Value);
                }

                logger.LogInformation("Пользователь {UserId} отключился", userId.Value);
            }

            // Завершить все активные звонки пользователя
            foreach (var session in callSessions.GetAllSessionsForUser(userId.Value))
            {
                callSessions.LeaveCall(session.CallId, userId.Value, out var shouldEnd);

                foreach (var p in session.ActiveParticipants.Values)
                    await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.CallParticipantLeft, session.CallId, userId.Value);

                if (shouldEnd)
                    await TerminateCallAsync(session.CallId, session.ChatId, CallEndReason.Ended);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    #endregion

    #region Chat Groups

    public async Task JoinChat(int chatId)
    {
        var userId = GetRequiredUserId();
        var result = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (result.IsFailure) throw new HubException(result.Error);

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        logger.LogDebug("Пользователь {UserId} присоединился к чату {ChatId}", userId, chatId);
    }

    public async Task LeaveChat(int chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        logger.LogDebug("Соединение {ConnectionId} покинуло чат {ChatId}", Context.ConnectionId, chatId);
    }

    #endregion

    #region Read Receipts

    public async Task<ChatReadInfoDto?> GetReadInfo(int chatId)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return null;

        using var scope = scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();
        var result = await svc.GetChatReadInfoAsync(userId.Value, chatId);
        return result.UnwrapOrDefault(logger);
    }

    public async Task MarkAsRead(int chatId, int? messageId = null)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();
        var result = await svc.MarkAsReadAsync(userId.Value, new MarkAsReadDto { ChatId = chatId, MessageId = messageId });
        if (!result.TryUnwrap(out var receipt, logger)) return;

        await Clients.Caller.SendAsync(HubMethods.Chat.UnreadCountUpdated, chatId, receipt.UnreadCount);
        await Clients.OthersInGroup($"chat_{chatId}")
            .SendAsync(HubMethods.Chat.MessageRead, chatId, userId.Value, receipt.LastReadMessageId, receipt.LastReadAt);

        logger.LogDebug("Пользователь {UserId} прочитал чат {ChatId}, unread={UnreadCount}",
            userId.Value, chatId, receipt.UnreadCount);
    }

    public async Task MarkMessageAsRead(int chatId, int messageId)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();
        var result = await svc.MarkMessageAsReadAsync(userId.Value, chatId, messageId);
        if (!result.TryUnwrap(out var receipt, logger)) return;

        await Clients.Caller.SendAsync(HubMethods.Chat.UnreadCountUpdated, chatId, receipt.UnreadCount);
        await Clients.OthersInGroup($"chat_{chatId}")
            .SendAsync(HubMethods.Chat.MessageRead, chatId, userId.Value, receipt.LastReadMessageId, receipt.LastReadAt);

        logger.LogDebug("Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}", userId.Value, messageId, chatId);
    }

    public async Task<AllUnreadCountsDto> GetUnreadCounts()
    {
        var userId = GetCurrentUserId();
        var fallback = new AllUnreadCountsDto { Chats = [], TotalUnread = 0 };
        if (!userId.HasValue) return fallback;

        using var scope = scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReadReceiptService>();
        var result = await svc.GetAllUnreadCountsAsync(userId.Value);
        return result.UnwrapOrFallback(fallback, logger);
    }

    #endregion

    #region Typing and Online

    public async Task SendTyping(int chatId)
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
            await Clients.OthersInGroup($"chat_{chatId}").SendAsync(HubMethods.Chat.UserTyping, chatId, userId.Value);
    }

    public async Task<List<int>> GetOnlineUsersInChat(int chatId)
    {
        var userId = GetRequiredUserId();
        var result = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (result.IsFailure) throw new HubException(result.Error);

        return [.. onlineUserService.FilterOnline(await accessControl.GetUserChatIdsAsync(chatId))];
    }

    public async Task SetStatus(int statusRaw, string? duration = null)
    {
        var userId = GetRequiredUserId();

        if (!Enum.IsDefined(typeof(UserStatusType), statusRaw))
        {
            logger.LogWarning("[SetStatus] Invalid statusRaw={StatusRaw} from userId={UserId}", statusRaw, userId);
            throw new HubException($"Неверный статус: {statusRaw}");
        }

        using var scope = scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IUserStatusService>();
        var result = await svc.SetStatusAsync(userId, (UserStatusType)statusRaw, duration.Parse());
        if (result.IsFailure) throw new HubException(result.Error);
    }

    #endregion

    #region Call — Initiate / Join / Leave / Decline / Cancel

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

        var allUserIds = memberIds.Append(userId).Distinct().ToList();
        await Task.WhenAll(allUserIds.Select(GetUserInfoAsync));

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
                session.PendingParticipants[memberId] = new CallParticipant { UserId = memberId };

            await Clients.Group($"user_{memberId}").SendAsync(HubMethods.Call.IncomingCall, invite);
        }

        var stateDto = await ToStateDtoAsync(session);
        await Clients.Caller.SendAsync(HubMethods.Call.CallStateUpdated, stateDto);
        await Clients.Group($"chat_{chatId}").SendAsync(HubMethods.Call.ActiveCallStarted, stateDto);

        await systemMessages.CreateCallStartedMessageAsync(chatId, userId);

        if (!isGroup)
            _ = StartRingingTimeoutAsync(session.CallId, chatId, RingingTimeoutSeconds);
    }

    public async Task JoinCall(string callId)
    {
        var userId = CurrentUserId;
        try
        {
            var session = callSessions.GetCall(callId);
            if (session == null)
            {
                LogJoinCallSessionNotFound(callId);
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
                logger.LogWarning("[MessengerHub] JoinCall: userId={UserId} не участник чата {ChatId}", userId, session.ChatId);
                await Clients.Caller.SendAsync(HubMethods.Call.CallError, "Нет доступа к чату");
                return;
            }

            if (!callSessions.JoinCall(callId, userId, Context.ConnectionId))
            {
                LogJoinCallReturnedFalse(callId, userId);
                await Clients.Caller.SendAsync(HubMethods.Call.CallEnded, callId, CallEndReason.Ended);
                return;
            }

            // Предзагружаем информацию о себе перед формированием состояния
            await GetUserInfoAsync(userId);

            var stateDto = await ToStateDtoAsync(session);
            await Clients.Caller.SendAsync(HubMethods.Call.CallStateUpdated, stateDto);

            var (name, avatar) = await GetUserInfoAsync(userId);
            var notification = new CallParticipantDto
            {
                UserId = userId,
                DisplayName = name ?? $"User {userId}",
                AvatarUrl = avatar
            };

            foreach (var p in session.ActiveParticipants.Values.Where(p => p.UserId != userId))
                await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.CallParticipantJoined, callId, notification);
        }
        catch (Exception ex)
        {
            LogJoinCallFailed(callId, userId, ex);
            try { await Clients.Caller.SendAsync(HubMethods.Call.CallError, "Ошибка подключения к звонку"); }
            catch { }
        }
    }

    public async Task LeaveCall(string callId)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        callSessions.LeaveCall(callId, userId, out var shouldEnd);

        foreach (var p in session.ActiveParticipants.Values)
            await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.CallParticipantLeft, callId, userId);

        if (shouldEnd)
            await TerminateCallAsync(callId, session.ChatId, CallEndReason.Ended);
        else if (session.IsGroupCall)
            await Clients.Group($"chat_{session.ChatId}").SendAsync(HubMethods.Call.ActiveCallUpdated, await ToStateDtoAsync(session));
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

    #endregion

    #region Call — Signals / Media

    public async Task SendSignal(SignalDto signal)
    {
        var session = callSessions.GetCall(signal.CallId);
        if (session == null) return;

        signal.FromUserId = CurrentUserId;
        if (!session.ActiveParticipants.TryGetValue(signal.TargetUserId, out var target)) return;

        await Clients.Client(target.ConnectionId).SendAsync(HubMethods.Call.ReceiveSignal, signal);
    }

    public async Task ToggleMute(string callId, bool isMuted)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        callSessions.SetMuted(callId, userId, isMuted);

        foreach (var p in session.ActiveParticipants.Values.Where(p => p.UserId != userId))
            await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.ParticipantMuteChanged, callId, userId, isMuted);
    }

    public async Task ToggleSpeaking(string callId, bool isSpeaking)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;
        if (!session.ActiveParticipants.ContainsKey(userId)) return;

        callSessions.SetSpeaking(callId, userId, isSpeaking);

        var others = session.ActiveParticipants.Values
            .Where(p => p.UserId != userId)
            .Select(p => p.ConnectionId)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        if (others.Count == 0) return;
        await Clients.Clients(others).SendAsync(HubMethods.Call.ParticipantSpeakingChanged, callId, userId, isSpeaking);
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

        var connectionIds = session.ActiveParticipants.Values
            .Select(p => p.ConnectionId)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        await Clients.Clients(connectionIds).SendAsync(HubMethods.Call.CallMessageReceived, message);
    }

    public async Task<CallStateDto?> GetCallState(int chatId)
    {
        try
        {
            var session = callSessions.GetActiveCallInChat(chatId);
            if (session == null) return null;
            if (!await accessControl.IsMemberAsync(CurrentUserId, chatId)) return null;
            return await ToStateDtoAsync(session);
        }
        catch (Exception ex)
        {
            LogGetCallStateFailed(chatId, ex);
            return null;
        }
    }

    #endregion

    #region Call — Private helpers

    private async Task TerminateCallAsync(string callId, int chatId, CallEndReason reason)
    {
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        var initiatorId = session.InitiatorId;

        foreach (var pending in session.PendingParticipants.Values)
            await Clients.Group($"user_{pending.UserId}").SendAsync(HubMethods.Call.CallEnded, callId, reason);

        foreach (var active in session.ActiveParticipants.Values)
            await Clients.Client(active.ConnectionId).SendAsync(HubMethods.Call.CallEnded, callId, reason);

        await Clients.Group($"chat_{chatId}").SendAsync(HubMethods.Call.ActiveCallEnded, callId);

        var duration = await callSessions.EndCallAsync(callId);

        if (reason != CallEndReason.Cancelled && reason != CallEndReason.Declined && duration > TimeSpan.FromSeconds(1))
        {
            await systemMessages.CreateCallEndedMessageAsync(chatId, initiatorId, duration);
        }
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
        catch (OperationCanceledException) { }
    }

    private async Task<(string? Name, string? Avatar)> GetUserInfoAsync(int userId)
    {
        if (_userCache.TryGetValue(userId, out var cached) && cached.IsCompletedSuccessfully)
            return cached.Result;

        var task = FetchUserInfoAsync(userId);
        _userCache[userId] = task;

        try
        {
            return await task;
        }
        catch
        {
            _userCache.TryRemove(userId, out _);
            return (null, null);
        }
    }

    private async Task<(string? Name, string? Avatar)> FetchUserInfoAsync(int userId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

            var user = await ctx.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new { u.Surname, u.Name, u.Midname, u.Username, u.Avatar })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                logger.LogWarning("[MessengerHub] Пользователь {UserId} не найден в БД", userId);
                return (null, null);
            }

            string? displayName = null;
            if (!string.IsNullOrWhiteSpace(user.Surname) || !string.IsNullOrWhiteSpace(user.Name))
            {
                displayName = string.Join(" ",
                    new[] { user.Surname, user.Name, user.Midname }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            displayName ??= user.Username;

            return (displayName, user.Avatar);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MessengerHub] FetchUserInfoAsync error для userId={UserId}", userId);
            return (null, null);
        }
    }

    private async Task<CallStateDto> ToStateDtoAsync(CallSession session)
    {
        var userIds = session.ActiveParticipants.Keys.Append(session.InitiatorId).Distinct().ToList();

        logger.LogInformation("[MessengerHub] ToStateDtoAsync: loading users {UserIds}", string.Join(",", userIds));

        await Task.WhenAll(userIds.Select(async id =>
        {
            var (name, avatar) = await GetUserInfoAsync(id);
            logger.LogInformation("[MessengerHub] ToStateDtoAsync: userId={UserId} name={Name} avatar={Avatar}",
                id, name ?? "NULL", avatar ?? "NULL");
        }));

        return callSessions.ToStateDto(session,
            uid => _userCache.TryGetValue(uid, out var t) && t.IsCompletedSuccessfully ? t.Result.Avatar : null,
            uid => _userCache.TryGetValue(uid, out var t) && t.IsCompletedSuccessfully ? t.Result.Name : null);
    }

    private async Task<string> GetChatNameAsync(int chatId)
        => await db.Chats.AsNoTracking().Where(c => c.Id == chatId).Select(c => c.Name).FirstOrDefaultAsync() ?? string.Empty;

    #endregion

    #region Identity helpers

    private int CurrentUserId
    {
        get
        {
            var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(raw, out var userId)) return userId;

            logger.LogError("[MessengerHub] Не удалось получить UserId. ConnectionId={ConnectionId}, Value={Value}", Context.ConnectionId, raw);
            throw new HubException("Невозможно идентифицировать пользователя.");
        }
    }

    private int? GetCurrentUserId()
    {
        var raw = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(raw) || !int.TryParse(raw, out var id) ? null : id;
    }

    private int GetRequiredUserId()
        => GetCurrentUserId() ?? throw new HubException("Пользователь не аутентифицирован");

    #endregion

    #region Logging

    [LoggerMessage(Level = LogLevel.Warning, Message = "[MessengerHub] Не удалось получить статус пользователя {UserId}: {Error}")]
    private partial void LogStatusGetFailed(int userId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[MessengerHub] GetChatReadInfoAsync завершился с ошибкой для UserId={UserId}, ChatId={ChatId}: {Error}")]
    private partial void LogReadInfoFailed(int userId, int chatId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[MessengerHub] JoinCall: сессия {CallId} не найдена")]
    private partial void LogJoinCallSessionNotFound(string callId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[MessengerHub] JoinCall: JoinCall вернул false для {CallId}, userId={UserId}")]
    private partial void LogJoinCallReturnedFalse(string callId, int userId);
    [LoggerMessage(Level = LogLevel.Error, Message = "[MessengerHub] JoinCall FAILED: callId={CallId}, userId={UserId}")]
    private partial void LogJoinCallFailed(string callId, int userId, Exception ex);
    [LoggerMessage(Level = LogLevel.Error, Message = "[MessengerHub] GetCallState failed для chatId={ChatId}")]
    private partial void LogGetCallStateFailed(int chatId, Exception ex);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Пользователь {UserId} прочитал чат {ChatId}, unread={UnreadCount}")]
    private partial void LogChatMarkedAsRead(int userId, int chatId, int unreadCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Пользователь {UserId} прочитал сообщение {MessageId} в чате {ChatId}")]
    private partial void LogMessageMarkedAsRead(int userId, int messageId, int chatId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} подключился, чатов: {ChatCount}")]
    private partial void LogUserConnected(int userId, int chatCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Звонок {CallId} в чате {ChatId} завершён по таймауту")]
    private partial void LogCallTimedOut(string callId, int chatId);

    #endregion
}