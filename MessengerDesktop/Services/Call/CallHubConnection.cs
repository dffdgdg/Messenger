using MessengerShared.DTO.Call;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Call;

public interface ICallHubConnection : IAsyncDisposable
{
    bool IsConnected { get; }
    Task InitiateCallAsync(int chatId);
    Task JoinCallAsync(string callId);
    Task LeaveCallAsync(string callId);
    Task DeclineCallAsync(string callId);
    Task CancelCallAsync(string callId);
    Task SendSignalAsync(WebRtcSignalDto signal);
    Task ToggleMuteAsync(string callId, bool isMuted);
    Task<CallStateDto?> GetCallStateAsync(int chatId);
    event Action<CallInviteDto>? IncomingCall;
    event Action<string, CallParticipantDto>? CallParticipantJoined;
    event Action<string, int>? CallParticipantLeft;
    event Action<string, CallEndReason>? CallEnded;
    event Action<WebRtcSignalDto>? SignalReceived;
    event Action<string, int, bool>? ParticipantMuteChanged;
    event Action<CallStateDto>? CallStateUpdated;
    event Action<CallStateDto>? ActiveCallStarted;
    event Action<CallStateDto>? ActiveCallUpdated;
    event Action<string>? ActiveCallEnded;
    event Action<string>? CallError;
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
}

public sealed partial class CallHubConnection : ICallHubConnection
{
    private readonly HubConnection _hub;
    private readonly ILogger<CallHubConnection> _logger;
    private int _disposed;

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

    public bool IsConnected => _hub.State == HubConnectionState.Connected;

    public CallHubConnection(ISessionStore sessionStore, ILogger<CallHubConnection> logger, string baseUrl)
    {
        _logger = logger;
        _hub = new HubConnectionBuilder().WithUrl($"{baseUrl}callHub", options => options.AccessTokenProvider = () => Task.FromResult(sessionStore.Token)).WithAutomaticReconnect().Build();
        SubscribeEvents();
    }

    private void SubscribeEvents()
    {
        _hub.On<CallInviteDto>("IncomingCall", dto =>
        {
            LogIncomingCall(dto.CallId, dto.ChatId);
            IncomingCall?.Invoke(dto);
        });

        _hub.On<string, CallParticipantDto>("CallParticipantJoined", (callId, participant) =>
        {
            LogParticipantJoined(participant.UserId, callId);
            CallParticipantJoined?.Invoke(callId, participant);
        });

        _hub.On<string, int>("CallParticipantLeft", (callId, userId) =>
        {
            LogParticipantLeft(userId, callId);
            CallParticipantLeft?.Invoke(callId, userId);
        });

        _hub.On<string, CallEndReason>("CallEnded", (callId, reason) =>
        {
            LogCallEnded(callId, reason);
            CallEnded?.Invoke(callId, reason);
        });

        _hub.On<WebRtcSignalDto>("ReceiveSignal", signal =>
        {
            LogSignalReceived(signal.Type, signal.FromUserId, signal.TargetUserId);
            SignalReceived?.Invoke(signal);
        });

        _hub.On<string, int, bool>("ParticipantMuteChanged", (callId, userId, isMuted) =>
            ParticipantMuteChanged?.Invoke(callId, userId, isMuted));

        _hub.On<CallStateDto>("CallStateUpdated", dto =>
        {
            LogCallStateUpdated(dto.CallId);
            CallStateUpdated?.Invoke(dto);
        });

        _hub.On<CallStateDto>("ActiveCallStarted", dto =>
        {
            LogActiveCallStarted(dto.CallId, dto.ChatId);
            ActiveCallStarted?.Invoke(dto);
        });

        _hub.On<CallStateDto>("ActiveCallUpdated", dto =>
            ActiveCallUpdated?.Invoke(dto));

        _hub.On<string>("ActiveCallEnded", callId =>
        {
            LogActiveCallEnded(callId);
            ActiveCallEnded?.Invoke(callId);
        });

        _hub.On<string>("CallError", message =>
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
        => SafeInvokeAsync("InitiateCall", chatId);

    public Task JoinCallAsync(string callId)
        => SafeInvokeAsync("JoinCall", callId);

    public Task LeaveCallAsync(string callId)
        => SafeInvokeAsync("LeaveCall", callId);

    public Task DeclineCallAsync(string callId)
        => SafeInvokeAsync("DeclineCall", callId);

    public Task CancelCallAsync(string callId)
        => SafeInvokeAsync("CancelCall", callId);

    public Task SendSignalAsync(WebRtcSignalDto signal)
        => SafeInvokeAsync("SendSignal", signal);

    public Task ToggleMuteAsync(string callId, bool isMuted)
        => SafeInvokeAsync("ToggleMute", callId, isMuted);

    public async Task<CallStateDto?> GetCallStateAsync(int chatId)
    {
        if (!IsConnected) return null;

        try
        {
            return await _hub.InvokeAsync<CallStateDto?>("GetCallState", chatId);
        }
        catch (Exception ex)
        {
            LogGetCallStateFailed(chatId, ex);
            return null;
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_hub.State != HubConnectionState.Disconnected) return;

        try
        {
            await _hub.StartAsync(ct);
            LogConnected();
        }
        catch (Exception ex)
        {
            LogConnectionError(ex);
            throw;
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

    #endregion
}