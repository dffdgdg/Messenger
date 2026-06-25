using API.Application.Features.Call;
using Microsoft.AspNetCore.SignalR;
using Shared.Contracts.Call;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Web.Hubs;

public sealed partial class MessengerHub
{
    private int MaxParticipants =>
        configuration.GetValue("CallSettings:MaxParticipants", 30);

    private const int RingingTimeoutSeconds = 60;

    #region Initiate / Join / Leave / Decline / Cancel

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
            await Clients.Caller.SendAsync(
                HubMethods.Call.CallError, "В этом чате уже идёт звонок");
            return;
        }

        var chatType = await accessControl.GetChatTypeAsync(chatId);
        var isGroup = chatType != ChatType.Contact;

        var session = await callSessions.CreateCallAsync(
            chatId, userId, Context.ConnectionId, chatType);

        if (session == null)
        {
            await Clients.Caller.SendAsync(
                HubMethods.Call.CallError, "Не удалось создать звонок");
            return;
        }

        var memberIds = await accessControl.GetChatMemberIdsAsync(chatId);

        // Предзагружаем инфо всех участников параллельно
        var allUserIds = memberIds.Append(userId).Distinct().ToList();
        await Task.WhenAll(allUserIds.Select(GetUserInfoAsync));

        // БЫЛО: var initiatorInfo = await GetUserInfoAsync(userId); — повторный вызов
        // СТАЛО: берём из кэша который уже заполнен WhenAll выше
        var initiatorInfo = _userInfoCache.TryGetValue(userId, out var cached)
            && cached.IsCompletedSuccessfully
            ? cached.Result
            : await GetUserInfoAsync(userId);

        var chatName = await userInfoService.GetChatNameAsync(chatId);

        var invite = new CallInviteDto
        {
            CallId = session.CallId,
            ChatId = chatId,
            ChatName = chatName,
            InitiatorId = userId,
            InitiatorName = initiatorInfo.DisplayName,
            InitiatorAvatar = initiatorInfo.Avatar,
            ActiveParticipantsCount = 1,
            IsGroupCall = isGroup,
            Mode = session.Mode
        };

        foreach (var memberId in memberIds.Where(id => id != userId))
        {
            if (!isGroup)
                session.PendingParticipants[memberId] =
                    new CallParticipant { UserId = memberId };

            await Clients.Group(UserGroup(memberId))
                .SendAsync(HubMethods.Call.IncomingCall, invite);
        }

        var stateDto = await BuildStateDtoAsync(session);
        await Clients.Caller.SendAsync(HubMethods.Call.CallStateUpdated, stateDto);
        await SendRelayEndpointAsync(session);
        await Clients.Group(ChatGroup(chatId))
            .SendAsync(HubMethods.Call.ActiveCallStarted, stateDto);

        await systemMessages.CreateCallStartedMessageAsync(chatId, userId);

        // Таймаут только для P2P — для группового не нужен
        // (групповой завершается когда все ушли через LeaveCall)
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
                await Clients.Caller.SendAsync(
                    HubMethods.Call.CallEnded, callId, CallEndReason.Ended);
                return;
            }

            if (session.ActiveParticipants.Count >= MaxParticipants)
            {
                await Clients.Caller.SendAsync(
                    HubMethods.Call.CallError, $"Максимум {MaxParticipants} участников");
                return;
            }

            if (!await accessControl.IsMemberAsync(userId, session.ChatId))
            {
                logger.LogWarning("JoinCall: userId={UserId} не участник чата {ChatId}", userId, session.ChatId);
                await Clients.Caller.SendAsync(HubMethods.Call.CallError, "Нет доступа к чату");
                return;
            }

            if (!callSessions.JoinCall(callId, userId, Context.ConnectionId))
            {
                LogJoinCallReturnedFalse(callId, userId);
                await Clients.Caller.SendAsync(HubMethods.Call.CallEnded, callId, CallEndReason.Ended);
                return;
            }

            await GetUserInfoAsync(userId);

            var stateDto = await BuildStateDtoAsync(session);
            await Clients.Caller.SendAsync(HubMethods.Call.CallStateUpdated, stateDto);
            await SendRelayEndpointAsync(session);

            var userInfo = await GetUserInfoAsync(userId);
            var notification = new CallParticipantDto
            {
                UserId = userId,
                DisplayName = userInfo.DisplayName,
                AvatarUrl = userInfo.Avatar
            };

            foreach (var p in session.ActiveParticipants.Values.Where(p => p.UserId != userId))
                await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.CallParticipantJoined, callId, notification);
        }
        catch (Exception ex)
        {
            LogJoinCallFailed(callId, userId, ex);
            try
            {
                await Clients.Caller.SendAsync(HubMethods.Call.CallError, "Ошибка подключения к звонку");
            }
            catch { /* соединение могло упасть */ }
        }
    }

    public async Task LeaveCall(string callId)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        if (!session.ActiveParticipants.ContainsKey(userId)
            && !session.PendingParticipants.ContainsKey(userId))
            return;

        callSessions.LeaveCall(callId, userId, out var shouldEnd);

        foreach (var p in session.ActiveParticipants.Values)
            await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.CallParticipantLeft, callId, userId);

        if (shouldEnd)
            await TerminateCallAsync(callId, session.ChatId, CallEndReason.Ended);
        else if (session.IsGroupCall)
            await Clients.Group(ChatGroup(session.ChatId)).SendAsync(HubMethods.Call.ActiveCallUpdated, await BuildStateDtoAsync(session));
    }

    public async Task DeclineCall(string callId)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);

        if (session == null)
        {
            await Clients.Caller.SendAsync(
                HubMethods.Call.CallEnded, callId, CallEndReason.Ended);
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

    #region Signals / Media

    public async Task SendSignal(SignalDto signal)
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
            await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.ParticipantMuteChanged, callId, userId, isMuted);
    }

    public async Task ToggleSpeaking(string callId, bool isSpeaking)
    {
        var userId = CurrentUserId;
        var session = callSessions.GetCall(callId);
        if (session == null) return;
        if (!session.ActiveParticipants.ContainsKey(userId)) return;

        callSessions.SetSpeaking(callId, userId, isSpeaking);

        var others = session.ActiveParticipants.Values.Where(p => p.UserId != userId && !string.IsNullOrEmpty(p.ConnectionId))
            .Select(p => p.ConnectionId).ToList();

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

        var userInfo = await GetUserInfoAsync(userId);
        var message = new CallChatMessageDto
        {
            CallId = callId,
            SenderId = userId,
            SenderName = userInfo.DisplayName,
            SenderAvatar = userInfo.Avatar,
            Text = text.Trim(),
            SentAt = DateTime.UtcNow
        };

        var connectionIds = session.ActiveParticipants.Values.Where(p => !string.IsNullOrEmpty(p.ConnectionId)).Select(p => p.ConnectionId).ToList();

        await Clients.Clients(connectionIds).SendAsync(HubMethods.Call.CallMessageReceived, message);
    }

    public async Task<CallStateDto?> GetCallState(int chatId)
    {
        try
        {
            var session = callSessions.GetActiveCallInChat(chatId);
            if (session == null) return null;
            if (!await accessControl.IsMemberAsync(CurrentUserId, chatId)) return null;
            return await BuildStateDtoAsync(session);
        }
        catch (Exception ex)
        {
            LogGetCallStateFailed(chatId, ex);
            return null;
        }
    }

    #endregion

    #region Private helpers

    private async Task HandleUserDisconnectedFromCallsAsync(int userId)
    {
        foreach (var session in callSessions.GetAllSessionsForUser(userId))
        {
            callSessions.LeaveCall(session.CallId, userId, out var shouldEnd);

            foreach (var p in session.ActiveParticipants.Values)
                await Clients.Client(p.ConnectionId).SendAsync(HubMethods.Call.CallParticipantLeft, session.CallId, userId);

            if (shouldEnd)
                await TerminateCallAsync(session.CallId, session.ChatId, CallEndReason.Ended);
        }
    }

    private async Task TerminateCallAsync(string callId, int chatId, CallEndReason reason)
    {
        var session = callSessions.GetCall(callId);
        if (session == null) return;

        var initiatorId = session.InitiatorId;

        foreach (var pending in session.PendingParticipants.Values)
            await Clients.Group(UserGroup(pending.UserId)).SendAsync(HubMethods.Call.CallEnded, callId, reason);

        foreach (var active in session.ActiveParticipants.Values)
            await Clients.Client(active.ConnectionId).SendAsync(HubMethods.Call.CallEnded, callId, reason);

        await Clients.Group(ChatGroup(chatId)).SendAsync(HubMethods.Call.ActiveCallEnded, callId);

        var duration = await callSessions.EndCallAsync(callId);

        if (reason is not (CallEndReason.Cancelled or CallEndReason.Declined)
            && duration > TimeSpan.FromSeconds(1))
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
        catch (OperationCanceledException) { /* звонок принят или отменён */ }
    }

    private async Task SendRelayEndpointAsync(CallSession session)
    {
        if (session.Mode == CallMode.ServerMixed)
        {
            var configHost = configuration.GetValue<string>("CallSettings:RelayHost");
            var host = !string.IsNullOrWhiteSpace(configHost) ? configHost : Context.GetHttpContext()?.Request.Host.Host ?? "localhost";

            await Clients.Caller.SendAsync(HubMethods.Call.RelayEndpoint, new RelayEndpointInfo
            {
                Host = host,
                Port = configuration.GetValue("CallSettings:RelayPort", 5276),
                CallId = session.CallId,
                Turn = null
            });
            return;
        }

        if (session.Mode == CallMode.PeerToPeer)
        {
            var turnCreds = turnCredentialService.GenerateCredentials(CurrentUserId);

            await Clients.Caller.SendAsync(HubMethods.Call.RelayEndpoint, new RelayEndpointInfo
            {
                Host = string.Empty,
                Port = 0,
                CallId = session.CallId,
                Turn = turnCreds
            });
        }
    }

    private async Task<CallStateDto> BuildStateDtoAsync(CallSession session)
    {
        var userIds = session.ActiveParticipants.Keys.Append(session.InitiatorId).Distinct().ToList();

        await Task.WhenAll(userIds.Select(GetUserInfoAsync));

        return callSessions.ToStateDto(session, avatarResolver: uid => _userInfoCache.TryGetValue(uid, out var t)
        && t.IsCompletedSuccessfully ? t.Result.Avatar : null, nameResolver: uid => _userInfoCache.TryGetValue(uid, out var t)
        && t.IsCompletedSuccessfully ? t.Result.DisplayName : null);
    }

    #endregion
}