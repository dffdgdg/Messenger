using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Shared.Dto.Call;
using Shared.Hubs;

namespace Desktop.Services.Call;

public sealed partial class CallHubConnection : ICallHubConnection
{
    private readonly HubConnection _hub;
    private readonly ILogger<CallHubConnection> _logger;
    private int _disposed;
    public event Action<CallChatMessageDto>? CallMessageReceived;

    public event Action<CallInviteDto>? IncomingCall;
    public event Action<string, CallParticipantDto>? CallParticipantJoined;
    public event Action<string, int>? CallParticipantLeft;
    public event Action<string, CallEndReason>? CallEnded;
    public event Action<WebRtcSignalDto>? SignalReceived;
    public event Action<string, int, bool>? ParticipantMuteChanged;
    public event Action<CallStateDto>? CallStateUpdated;
    public event Action<CallStateDto>? ActiveCallStarted;
    public event Action<CallStateDto>? ActiveCallUpdated;
    public event Action<string>? ActiveCallEnded;
    public event Action<string>? CallError;
    public event Action<string, int, bool>? ParticipantSpeakingChanged;

    public bool IsConnected => _hub.State == HubConnectionState.Connected;

    public CallHubConnection(ISessionStore sessionStore, ILogger<CallHubConnection> logger, string baseUrl)
    {
        _logger = logger;
        _hub = new HubConnectionBuilder().WithUrl($"{baseUrl}chatHub", options => options.AccessTokenProvider = () => Task.FromResult(sessionStore.Token)).WithAutomaticReconnect().Build();
        SubscribeEvents();
    }

    private void SubscribeEvents()
    {
        _hub.On<CallInviteDto>(HubMethods.Call.IncomingCall, dto =>
        {
            LogIncomingCall(dto.CallId, dto.ChatId);
            IncomingCall?.Invoke(dto);
        });

        _hub.On<string, CallParticipantDto>(HubMethods.Call.CallParticipantJoined, (callId, participant) =>
        {
            LogParticipantJoined(participant.UserId, callId);
            CallParticipantJoined?.Invoke(callId, participant);
        });

        _hub.On<string, int>(HubMethods.Call.CallParticipantLeft, (callId, userId) =>
        {
            LogParticipantLeft(userId, callId);
            CallParticipantLeft?.Invoke(callId, userId);
        });

        _hub.On<string, CallEndReason>(HubMethods.Call.CallEnded, (callId, reason) =>
        {
            LogCallEnded(callId, reason);
            CallEnded?.Invoke(callId, reason);
        });

        _hub.On<WebRtcSignalDto>(HubMethods.Call.ReceiveSignal, signal =>
        {
            LogSignalReceived(signal.Type, signal.FromUserId, signal.TargetUserId);
            SignalReceived?.Invoke(signal);
        });
        _hub.On<CallChatMessageDto>(HubMethods.Call.CallMessageReceived, dto => CallMessageReceived?.Invoke(dto));

        _hub.On<string, int, bool>(HubMethods.Call.ParticipantMuteChanged, (callId, userId, isMuted) =>
            ParticipantMuteChanged?.Invoke(callId, userId, isMuted));

        _hub.On<string, int, bool>(HubMethods.Call.ParticipantSpeakingChanged, (callId, userId, isSpeaking) =>
            ParticipantSpeakingChanged?.Invoke(callId, userId, isSpeaking));

        _hub.On<CallStateDto>(HubMethods.Call.CallStateUpdated, dto =>
        {
            LogCallStateUpdated(dto.CallId);
            CallStateUpdated?.Invoke(dto);
        });

        _hub.On<CallStateDto>(HubMethods.Call.ActiveCallStarted, dto =>
        {
            LogActiveCallStarted(dto.CallId, dto.ChatId);
            ActiveCallStarted?.Invoke(dto);
        });

        _hub.On<CallStateDto>(HubMethods.Call.ActiveCallUpdated, dto =>
            ActiveCallUpdated?.Invoke(dto));

        _hub.On<string>(HubMethods.Call.ActiveCallEnded, callId =>
        {
            LogActiveCallEnded(callId);
            ActiveCallEnded?.Invoke(callId);
        });

        _hub.On<string>(HubMethods.Call.CallError, message =>
        {
            LogCallError(message);
            CallError?.Invoke(message);
        });

        _hub.Reconnected += async connectionId =>
        {
            LogReconnected(connectionId);
            await Task.CompletedTask;
        };

        _hub.Reconnecting += ex =>
        {
            LogReconnecting(ex);
            return Task.CompletedTask;
        };
    }

    public Task InitiateCallAsync(int chatId)
    => SafeInvokeAsync(HubMethods.CallInvoke.InitiateCall, chatId);

    public Task LeaveCallAsync(string callId)
        => SafeInvokeAsync(HubMethods.CallInvoke.LeaveCall, callId);

    public Task DeclineCallAsync(string callId)
        => SafeInvokeAsync(HubMethods.CallInvoke.DeclineCall, callId);

    public Task CancelCallAsync(string callId)
        => SafeInvokeAsync(HubMethods.CallInvoke.CancelCall, callId);

    public Task SendSignalAsync(WebRtcSignalDto signal)
        => SafeInvokeAsync(HubMethods.CallInvoke.SendSignal, signal);

    public Task SendCallMessageAsync(string callId, string text)
        => SafeInvokeAsync(HubMethods.CallInvoke.SendCallMessage, callId, text);

    public Task ToggleMuteAsync(string callId, bool isMuted)
        => SafeInvokeAsync(HubMethods.CallInvoke.ToggleMute, callId, isMuted);

    public Task ToggleSpeakingAsync(string callId, bool isSpeaking)
        => SafeInvokeAsync(HubMethods.CallInvoke.ToggleSpeaking, callId, isSpeaking);

    public async Task<CallStateDto?> GetCallStateAsync(int chatId)
    {
        if (!IsConnected) return null;
        try { return await _hub.InvokeAsync<CallStateDto?>(HubMethods.CallInvoke.GetCallState, chatId); }
        catch (Exception ex) { LogGetCallStateFailed(chatId, ex); return null; }
    }
    public async Task JoinCallAsync(string callId)
    {
        if (!IsConnected)
        {
            LogHubNotConnected(HubMethods.CallInvoke.JoinCall);
            return;
        }
        try
        {
            await _hub.InvokeAsync(HubMethods.CallInvoke.JoinCall, callId);
        }
        catch (Exception ex)
        {
            LogInvokeError(HubMethods.CallInvoke.JoinCall, ex);
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_hub.State == HubConnectionState.Connected) return;

        if (_hub.State == HubConnectionState.Connecting)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (_hub.State == HubConnectionState.Connecting && DateTime.UtcNow < deadline)
                await Task.Delay(100, ct);
            return;
        }

        const int maxRetries = 10;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await _hub.StartAsync(ct);
                LogConnected();
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                LogServerNotReady(i + 1, maxRetries);
                if (i == maxRetries - 1) throw;
                await Task.Delay(2000, ct);
            }
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _hub.StopAsync();
        }
        catch (Exception ex)
        {
            LogDisconnectionError(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await DisconnectAsync();
        await _hub.DisposeAsync();
    }

    private async Task SafeInvokeAsync(string method, params object?[] args)
    {
        if (!IsConnected)
        {
            LogHubNotConnected(method);
            return;
        }

        try
        {
            await _hub.SendCoreAsync(method, args);
        }
        catch (Exception ex)
        {
            LogInvokeError(method, ex);
        }
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Debug, Message = "IncomingCall: {CallId} chatId={ChatId}")]
    private partial void LogIncomingCall(string callId, int chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CallParticipantJoined: {UserId} → {CallId}")]
    private partial void LogParticipantJoined(int userId, string callId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CallParticipantLeft: {UserId} ← {CallId}")]
    private partial void LogParticipantLeft(int userId, string callId);

    [LoggerMessage(Level = LogLevel.Information, Message = "CallEnded: {CallId}, reason={Reason}")]
    private partial void LogCallEnded(string callId, CallEndReason reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ReceiveSignal: {Type} from={FromUserId} to={TargetUserId}")]
    private partial void LogSignalReceived(string type, int fromUserId, int targetUserId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CallStateUpdated: {CallId}")]
    private partial void LogCallStateUpdated(string callId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ActiveCallStarted: {CallId} chatId={ChatId}")]
    private partial void LogActiveCallStarted(string callId, int chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ActiveCallEnded: {CallId}")]
    private partial void LogActiveCallEnded(string callId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CallError: {Message}")]
    private partial void LogCallError(string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "CallHub reconnected: {ConnectionId}")]
    private partial void LogReconnected(string? connectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CallHub reconnecting...")]
    private partial void LogReconnecting(Exception? ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GetCallState failed для chatId={ChatId}")]
    private partial void LogGetCallStateFailed(int chatId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "CallHub подключён")]
    private partial void LogConnected();

    [LoggerMessage(Level = LogLevel.Error, Message = "Ошибка подключения CallHub")]
    private partial void LogConnectionError(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ошибка отключения CallHub")]
    private partial void LogDisconnectionError(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CallHub не подключён, пропуск {Method}")]
    private partial void LogHubNotConnected(string method);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ошибка вызова {Method}")]
    private partial void LogInvokeError(string method, Exception ex);
    [LoggerMessage(Level = LogLevel.Warning, Message = "CallHub: сервер не готов (503), попытка {Attempt}/{MaxRetries}...")]
    private partial void LogServerNotReady(int attempt, int maxRetries);

    #endregion
}