using Shared.Dto.Call;

namespace Desktop.Services.Abstractions;

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
    Task ToggleSpeakingAsync(string callId, bool isSpeaking);
    event Action<string, int, bool>? ParticipantSpeakingChanged;
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
    Task SendCallMessageAsync(string callId, string text);
    event Action<CallChatMessageDto>? CallMessageReceived;
}