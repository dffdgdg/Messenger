using Core.Services.Features.Call;
using Core.Services.Features.Call.WebRtc;
using Microsoft.Extensions.Logging;
using Shared.Dto.Call;

namespace Core.Services.Call;

public sealed partial class CallService : ICallService
{
    private readonly ICallHubConnection _hub;
    private readonly ISessionStore _session;
    private readonly ICallAudioService _audio;
    private readonly ILogger<CallService> _logger;
    private readonly IWebRtcPeerConnectionFactory _peerFactory;

    private string? _activeCallId;
    private int? _activeChatId;
    private bool _disposed;
    private WebRtcManager? _webRtc;
    private IceServerConfig? _pendingIceConfig;

    public event Action<bool>? MuteChanged;
    public event Action? CallStarted;
    public event Action? CallEnded;
    public event Action<int, bool>? ParticipantSpeakingChanged;

    public bool IsInCall => _activeCallId != null;
    public bool IsMuted => _audio.IsMuted;
    public string? ActiveCallId => _activeCallId;
    public int? ActiveChatId => _activeChatId;

    public CallService(ICallHubConnection hub, ISessionStore session, ICallAudioService audio,
        ILogger<CallService> logger, IWebRtcPeerConnectionFactory peerFactory)
    {
        _hub = hub;
        _session = session;
        _audio = audio;
        _logger = logger;
        _peerFactory = peerFactory;

        SubscribeHubEvents();
    }

    public async Task StartCallAsync(int chatId)
    {
        if (IsInCall) return;

        _activeChatId = chatId;
        SubscribeAudioEvents();

        try { _audio.Start(); }
        catch (Exception ex) { LogAudioUnavailable(ex); }

        await _hub.InitiateCallAsync(chatId);
    }

    public async Task JoinCallAsync(string callId, int chatId)
    {
        if (IsInCall && _activeCallId == callId) return;
        if (IsInCall) await LeaveCallAsync();

        if (!_hub.IsConnected)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_hub.IsConnected && DateTime.UtcNow < deadline)
                await Task.Delay(100);

            if (!_hub.IsConnected) { LogHubConnectionFailed(); return; }
        }

        _activeCallId = callId;
        _activeChatId = chatId;
        _pendingIceConfig = null;

        _webRtc = new WebRtcManager(_session.UserId ?? 0, callId, _logger, _peerFactory, _pendingIceConfig);

        _webRtc.PeerReady += OnPeerReady;
        _webRtc.AudioReceived += OnWebRtcAudioReceived;
        _webRtc.SignalingReady += OnWebRtcSignalingReady;

        SubscribeAudioEvents();

        try { _audio.Start(); }
        catch (Exception ex) { LogAudioUnavailable(ex); }

        await _hub.JoinCallAsync(callId);
        CallStarted?.Invoke();
    }

    public async Task LeaveCallAsync()
    {
        if (!IsInCall) return;

        var callId = _activeCallId!;
        _activeCallId = null;
        _activeChatId = null;

        await _hub.LeaveCallAsync(callId);
        await CleanupAsync();
        CallEnded?.Invoke();
    }

    public async Task DeclineCallAsync(string callId) =>
        await _hub.DeclineCallAsync(callId);

    public async Task CancelCallAsync()
    {
        if (!IsInCall) return;

        var callId = _activeCallId!;
        _activeCallId = null;
        _activeChatId = null;

        await _hub.CancelCallAsync(callId);
        await CleanupAsync();
        CallEnded?.Invoke();
    }

    public async Task ToggleMuteAsync()
    {
        _audio.SetMuted(!_audio.IsMuted);

        if (IsInCall)
            await _hub.ToggleMuteAsync(_activeCallId!, _audio.IsMuted);

        MuteChanged?.Invoke(_audio.IsMuted);
    }

    private void OnPeerReady(int peerId)
    {
        _audio.AddParticipant(peerId);
        LogPeerReady(peerId);
    }

    private void OnWebRtcAudioReceived(int peerId, byte[] opusData)
    {
        _audio.ReceiveEncodedAudio(peerId, opusData, opusData.Length);
    }

    private async void OnWebRtcSignalingReady(SignalDto signal)
    {
        try { await _hub.SendSignalAsync(signal); }
        catch (Exception ex) { LogSignalingError(ex); }
    }

    private void OnEncodedFrame(byte[] opusData, int length)
    {
        if (!IsInCall || _webRtc == null) return;

        var data = opusData.Length == length ? opusData : opusData[..length];
        _webRtc.SendAudioToAll(data);
    }

    private void SubscribeHubEvents()
    {
        _hub.CallStateUpdated += OnCallStateUpdated;
        _hub.CallParticipantJoined += OnParticipantJoined;
        _hub.CallParticipantLeft += OnParticipantLeft;
        _hub.CallEnded += OnCallEndedRemotely;
        _hub.SignalReceived += OnSignalReceived;
        _hub.ParticipantSpeakingChanged += OnParticipantSpeakingChanged;
        _hub.RelayEndpoint += OnRelayEndpointReceived;
    }

    private void UnsubscribeHubEvents()
    {
        _hub.CallStateUpdated -= OnCallStateUpdated;
        _hub.CallParticipantJoined -= OnParticipantJoined;
        _hub.CallParticipantLeft -= OnParticipantLeft;
        _hub.CallEnded -= OnCallEndedRemotely;
        _hub.SignalReceived -= OnSignalReceived;
        _hub.ParticipantSpeakingChanged -= OnParticipantSpeakingChanged;
        _hub.RelayEndpoint -= OnRelayEndpointReceived;
    }

    private void OnRelayEndpointReceived(RelayEndpointInfo endpoint)
    {
        if (string.IsNullOrEmpty(endpoint.CallId)) return;
        if (endpoint.Turn == null && endpoint.Port > 0)
        {
            LogRelayEndpointIgnored(endpoint.Host, endpoint.Port);
            return;
        }

        if (endpoint.Turn != null)
        {
            var iceConfig = IceServerConfig.FromRelayEndpoint(endpoint);

            if (_webRtc != null)
            {
                _webRtc.UpdateIceConfig(iceConfig);
                LogTurnCredentialsReceived(endpoint.Turn.Username);
            }
            else
            {
                _pendingIceConfig = iceConfig;
                LogTurnCredentialsPending();
            }
        }
    }

    private void OnParticipantSpeakingChanged(string callId, int userId, bool isSpeaking)
    {
        if (callId != _activeCallId) return;
        ParticipantSpeakingChanged?.Invoke(userId, isSpeaking);
    }

    private void OnCallStateUpdated(CallStateDto state)
    {
        bool wasInCall = _activeCallId != null;
        _activeCallId = state.CallId;

        if (_webRtc == null)
        {
            _webRtc = new WebRtcManager(_session.UserId ?? 0, state.CallId, _logger, _peerFactory, _pendingIceConfig);

            _pendingIceConfig = null;

            _webRtc.PeerReady += OnPeerReady;
            _webRtc.AudioReceived += OnWebRtcAudioReceived;
            _webRtc.SignalingReady += OnWebRtcSignalingReady;
        }

        if (!wasInCall) CallStarted?.Invoke();

        var myUserId = _session.UserId ?? 0;

        foreach (var p in state.Participants)
        {
            if (p.UserId == myUserId) continue;
            if (!_webRtc.HasPeer(p.UserId))
            {
                if (myUserId > p.UserId)
                    _ = _webRtc.InitiateAsync(p.UserId);
            }
        }
    }

    private void OnParticipantJoined(string callId, CallParticipantDto participant)
    {
        if (callId != _activeCallId || _webRtc == null) return;

        int myId = _session.UserId ?? 0;
        if (participant.UserId == myId) return;

        if (myId > participant.UserId)
            _ = _webRtc.InitiateAsync(participant.UserId);
    }

    private async void OnParticipantLeft(string callId, int userId)
    {
        if (callId != _activeCallId || _webRtc == null) return;

        _audio.RemoveParticipant(userId);
        await _webRtc.RemovePeerAsync(userId);
    }

    private void OnCallEndedRemotely(string callId, CallEndReason reason)
    {
        if (callId != _activeCallId) return;

        _activeCallId = null;
        _activeChatId = null;
        _ = CleanupAsync();
        CallEnded?.Invoke();
    }

    private async void OnSignalReceived(SignalDto signal)
    {
        if (signal.CallId != _activeCallId || _webRtc == null) return;
        if (signal.FromUserId == (_session.UserId ?? 0)) return;
        if (signal.Type == "udp-endpoint") return;

        if (signal.Type is "webrtc-offer" or "webrtc-answer" or "webrtc-ice")
            await _webRtc.HandleSignalAsync(signal);
    }

    private void SubscribeAudioEvents()
    {
        _audio.OnEncodedFrame -= OnEncodedFrame;
        _audio.OnEncodedFrame += OnEncodedFrame;
        _audio.SpeakingStateChanged -= OnSpeakingStateChanged;
        _audio.SpeakingStateChanged += OnSpeakingStateChanged;
    }

    private void UnsubscribeAudioEvents()
    {
        _audio.OnEncodedFrame -= OnEncodedFrame;
        _audio.SpeakingStateChanged -= OnSpeakingStateChanged;
    }

    private void OnSpeakingStateChanged(bool isSpeaking)
    {
        if (_activeCallId == null) return;
        _ = _hub.ToggleSpeakingAsync(_activeCallId, isSpeaking)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    LogSpeakingToggleFailed(t.Exception?.GetBaseException());
            }, TaskScheduler.Default);
    }

    private async Task CleanupAsync()
    {
        UnsubscribeAudioEvents();

        if (_webRtc != null)
        {
            _webRtc.PeerReady -= OnPeerReady;
            _webRtc.AudioReceived -= OnWebRtcAudioReceived;
            _webRtc.SignalingReady -= OnWebRtcSignalingReady;

            await _webRtc.DisposeAsync();
            _webRtc = null;
        }

        _audio.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeHubEvents();
        UnsubscribeAudioEvents();

        if (IsInCall) await LeaveCallAsync();
        else await CleanupAsync();
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Warning, Message = "Аудио недоступно, продолжаем без микрофона")]
    private partial void LogAudioUnavailable(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "JoinCallAsync: CallHub так и не подключился, отмена")]
    private partial void LogHubConnectionFailed();

    [LoggerMessage(Level = LogLevel.Information, Message = "WebRTC DataChannel готов с peer {PeerId}")]
    private partial void LogPeerReady(int peerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WebRTC signaling send error")]
    private partial void LogSignalingError(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ToggleSpeaking SignalR error")]
    private partial void LogSpeakingToggleFailed(Exception? ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "RelayEndpoint для UDP relay {Host}:{Port} — игнорируем для WebRTC")]
    private partial void LogRelayEndpointIgnored(string host, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "TURN credentials получены для username={Username}")]
    private partial void LogTurnCredentialsReceived(string username);

    [LoggerMessage(Level = LogLevel.Debug, Message = "TURN credentials сохранены в pending — WebRTC менеджер ещё не создан")]
    private partial void LogTurnCredentialsPending();

    #endregion
}