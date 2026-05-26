using Desktop.Services.Features.Call;
using Microsoft.Extensions.Logging;
using Shared.Dto.Call;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Desktop.Services.Call;

public sealed partial class CallService : ICallService
{
    private readonly ICallHubConnection _hub;
    private readonly ISessionStore _session;
    private readonly CallAudioService _audio;
    private readonly ILogger<CallService> _logger;

    private string? _activeCallId;
    private int? _activeChatId;
    private bool _disposed;
    private int _sendSequence;

    private UdpClient? _udpClient;
    private int _localUdpPort;
    private CancellationTokenSource? _receiveCts;

    private readonly ConcurrentDictionary<int, IPEndPoint> _peerEndpoints = new();
    private readonly ConcurrentDictionary<int, bool> _endpointAnnounced = new();

    public event Action<bool>? MuteChanged;
    public event Action? CallStarted;
    public event Action? CallEnded;

    public bool IsInCall => _activeCallId != null;
    public bool IsMuted => _audio.IsMuted;
    public string? ActiveCallId => _activeCallId;
    public int? ActiveChatId => _activeChatId;

    public CallService(ICallHubConnection hub, ISessionStore session, CallAudioService audio, ILogger<CallService> logger)
    {
        _hub = hub;
        _session = session;
        _audio = audio;
        _logger = logger;

        _audio.OnEncodedFrame += SendAudioToAllPeers;
        SubscribeHubEvents();
    }

    public async Task StartCallAsync(int chatId)
    {
        if (IsInCall) return;
        if (_udpClient != null) Cleanup();

        _activeChatId = chatId;
        InitUdp();
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
            LogWaitingForHub();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_hub.IsConnected && DateTime.UtcNow < deadline)
                await Task.Delay(100);

            if (!_hub.IsConnected)
            {
                LogHubConnectionFailed();
                return;
            }
        }

        _activeCallId = callId;
        _activeChatId = chatId;

        InitUdp();
        SubscribeAudioEvents();

        try
        {
            _audio.Start();
        }
        catch (Exception ex)
        {
            LogAudioUnavailable(ex);
        }

        await _hub.JoinCallAsync(callId);
        CallStarted?.Invoke();
    }
    private void SubscribeAudioEvents()
    {
        _audio.SpeakingStateChanged -= OnSpeakingStateChanged;
        _audio.SpeakingStateChanged += OnSpeakingStateChanged;
    }

    public async Task LeaveCallAsync()
    {
        if (!IsInCall) return;

        var callId = _activeCallId!;
        _activeCallId = null;
        _activeChatId = null;

        await _hub.LeaveCallAsync(callId);
        Cleanup();
        CallEnded?.Invoke();
    }

    public async Task DeclineCallAsync(string callId) => await _hub.DeclineCallAsync(callId);

    public async Task CancelCallAsync()
    {
        if (!IsInCall) return;

        var callId = _activeCallId!;
        _activeCallId = null;
        _activeChatId = null;

        await _hub.CancelCallAsync(callId);
        Cleanup();
        CallEnded?.Invoke();
    }

    public async Task ToggleMuteAsync()
    {
        _audio.SetMuted(!_audio.IsMuted);

        if (IsInCall)
            await _hub.ToggleMuteAsync(_activeCallId!, _audio.IsMuted);

        MuteChanged?.Invoke(_audio.IsMuted);
    }
    private List<string> _localIps = [];
    private void InitUdp()
    {
        _localIps = GetAllLocalIpAddresses();
        _udpClient = new UdpClient(0);
        _localUdpPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

        LogUdpEndpoint(string.Join(", ", _localIps), _localUdpPort);

        _receiveCts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    private static List<string> GetAllLocalIpAddresses()
        => [.. Dns.GetHostEntry(Dns.GetHostName()).AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork
            && !IPAddress.IsLoopback(a)).Select(a => a.ToString())];

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                ProcessUdpPacket(result.Buffer, result.Buffer.Length);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    LogUdpReceiveError(ex);
            }
        }
    }

    private void ProcessUdpPacket(byte[] data, int length)
    {
        if (length < 9) return;

        var fromUserId = BitConverter.ToInt32(data, 0);
        var opusLength = length - 8;
        var opusData = new byte[opusLength];
        Buffer.BlockCopy(data, 8, opusData, 0, opusLength);

        _audio.ReceiveEncodedAudio(fromUserId, opusData, opusLength);
    }

    private void SendAudioToAllPeers(byte[] opusData, int opusLength)
    {
        if (!IsInCall || _udpClient == null) return;

        if (_session.UserId is not int userId || userId <= 0)
        {
            LogNoPeers();
            return;
        }

        if (_peerEndpoints.IsEmpty)
        {
            LogNoPeers();
            return;
        }

        var seq = Interlocked.Increment(ref _sendSequence);
        var packet = new byte[8 + opusLength];
        BitConverter.TryWriteBytes(packet.AsSpan(0), userId);
        BitConverter.TryWriteBytes(packet.AsSpan(4), seq);
        Buffer.BlockCopy(opusData, 0, packet, 8, opusLength);

        foreach (var (peerId, endpoint) in _peerEndpoints)
        {
            try
            {
                _udpClient.Send(packet, packet.Length, endpoint);
            }
            catch (Exception ex)
            {
                LogUdpSendError(peerId, ex);
            }
        }
    }

    private async Task AnnounceUdpEndpointAsync(int targetUserId)
    {
        if (_activeCallId == null || _localIps.Count == 0)
        {
            LogAnnouncementSkipped(_activeCallId, string.Join(",", _localIps));
            return;
        }

        var payload = string.Join(",", _localIps.Select(ip => $"{ip}:{_localUdpPort}"));

        await _hub.SendSignalAsync(new WebRtcSignalDto
        {
            CallId = _activeCallId,
            TargetUserId = targetUserId,
            Type = "udp-endpoint",
            Payload = payload
        });

        LogEndpointAnnounced(payload, _localUdpPort, targetUserId);
    }

    private void HandleUdpEndpointSignal(WebRtcSignalDto signal)
    {
        LogEndpointReceived(signal.FromUserId, signal.Payload);

        var candidates = signal.Payload.Split(',');

        IPEndPoint? bestEndpoint = null;
        int bestMatchOctets = -1;
        string? selectedCandidate = null;

        foreach (var candidate in candidates)
        {
            var parts = candidate.Trim().Split(':');
            if (parts.Length != 2) continue;
            if (!IPAddress.TryParse(parts[0], out var ip)) continue;
            if (!int.TryParse(parts[1], out var port)) continue;

            var endpoint = new IPEndPoint(ip, port);

            if (IsInSameSubnet(ip, out int matchOctets))
            {
                if (matchOctets > bestMatchOctets)
                {
                    bestMatchOctets = matchOctets;
                    bestEndpoint = endpoint;
                    selectedCandidate = candidate;
                }
                continue;
            }

            if (bestEndpoint == null)
            {
                bestEndpoint = endpoint;
                selectedCandidate = candidate;
            }
        }

        if (bestEndpoint == null) return;

        LogEndpointSelected(signal.FromUserId, selectedCandidate!, signal.Payload);

        _peerEndpoints[signal.FromUserId] = bestEndpoint;

        LogPeerEndpointRegistered(signal.FromUserId, bestEndpoint);
    }

    private bool IsInSameSubnet(IPAddress remoteIp, out int matchOctets)
    {
        matchOctets = 0;
        var remoteBytes = remoteIp.GetAddressBytes();

        foreach (var localIpStr in _localIps)
        {
            if (!IPAddress.TryParse(localIpStr, out var localIp)) continue;
            var localBytes = localIp.GetAddressBytes();

            int matches = 0;
            for (int i = 0; i < 4; i++)
            {
                if (remoteBytes[i] == localBytes[i]) matches++;
                else break;
            }
            matchOctets = Math.Max(matchOctets, matches);
        }
        return matchOctets >= 3;
    }

    private void SubscribeHubEvents()
    {
        _hub.ParticipantSpeakingChanged += OnParticipantSpeakingChanged;
        _hub.CallStateUpdated += OnCallStateUpdated;
        _hub.CallParticipantJoined += OnParticipantJoined;
        _hub.CallParticipantLeft += OnParticipantLeft;
        _hub.CallEnded += OnCallEndedRemotely;
        _hub.SignalReceived += OnSignalReceived;
    }

    private void OnParticipantSpeakingChanged(string callId, int userId, bool isSpeaking)
    {
        if (callId != _activeCallId) return;
        ParticipantSpeakingChanged?.Invoke(userId, isSpeaking);
    }

    public event Action<int, bool>? ParticipantSpeakingChanged;

    private void UnsubscribeHubEvents()
    {
        _hub.ParticipantSpeakingChanged -= OnParticipantSpeakingChanged;
        _hub.CallStateUpdated -= OnCallStateUpdated;
        _hub.CallParticipantJoined -= OnParticipantJoined;
        _hub.CallParticipantLeft -= OnParticipantLeft;
        _hub.CallEnded -= OnCallEndedRemotely;
        _hub.SignalReceived -= OnSignalReceived;
    }

    private void OnCallStateUpdated(CallStateDto state)
    {
        bool wasInCall = _activeCallId != null;
        _activeCallId = state.CallId;

        if (!wasInCall)
            CallStarted?.Invoke();

        var myUserId = _session.UserId ?? 0;
        foreach (var p in state.Participants)
        {
            if (p.UserId == myUserId) continue;

            bool isNew = !_audio.HasParticipant(p.UserId);
            if (isNew)
                _audio.AddParticipant(p.UserId);

            if (isNew || !wasInCall)
            {
                _ = AnnounceUdpEndpointAsync(p.UserId);
            }
        }
    }

    private void OnSpeakingStateChanged(bool isSpeaking)
    {
        if (_activeCallId == null) return;

        _ = _hub.ToggleSpeakingAsync(_activeCallId, isSpeaking).ContinueWith(t =>
        {
            if (t.IsFaulted)
                LogSpeakingToggleFailed(t.Exception?.GetBaseException());
        }, TaskScheduler.Default);
    }

    private void OnParticipantJoined(string callId, CallParticipantDto participant)
    {
        if (callId != _activeCallId) return;
        if (_session.UserId is int myId && participant.UserId == myId) return;

        _audio.AddParticipant(participant.UserId);
        _ = AnnounceUdpEndpointAsync(participant.UserId);
    }

    private void OnParticipantLeft(string callId, int userId)
    {
        if (callId != _activeCallId) return;

        _audio.RemoveParticipant(userId);
        _peerEndpoints.TryRemove(userId, out _);
        _endpointAnnounced.TryRemove(userId, out _);
    }

    private void OnCallEndedRemotely(string callId, CallEndReason reason)
    {
        if (callId != _activeCallId) return;

        _activeCallId = null;
        _activeChatId = null;
        Cleanup();
        CallEnded?.Invoke();
    }

    private void OnSignalReceived(WebRtcSignalDto signal)
    {
        if (signal.CallId != _activeCallId) return;
        if (signal.Type == "udp-endpoint")
        {
            var myUserId = _session.UserId ?? 0;
            if (signal.FromUserId == myUserId) return;
            if (!_audio.HasParticipant(signal.FromUserId))
            {
                LogEndpointSkippedNotInCall(signal.FromUserId);
                return;
            }
            HandleUdpEndpointSignal(signal);
        }
    }

    private void Cleanup()
    {
        LogCleanupStarted(new StackTrace().ToString());
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        _udpClient?.Dispose();
        _udpClient = null;

        _peerEndpoints.Clear();
        _endpointAnnounced.Clear();
        _localIps.Clear();

        _audio.SpeakingStateChanged -= OnSpeakingStateChanged;

        _audio.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeHubEvents();

        _audio.OnEncodedFrame -= SendAudioToAllPeers;
        _audio.SpeakingStateChanged -= OnSpeakingStateChanged;

        if (IsInCall) await LeaveCallAsync();
        else Cleanup();
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Warning, Message = "Аудио недоступно, продолжаем без микрофона")]
    private partial void LogAudioUnavailable(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "JoinCallAsync: ожидание подключения CallHub...")]
    private partial void LogWaitingForHub();

    [LoggerMessage(Level = LogLevel.Warning, Message = "JoinCallAsync: CallHub так и не подключился, отмена")]
    private partial void LogHubConnectionFailed();

    [LoggerMessage(Level = LogLevel.Information, Message = "UDP endpoint: {Ip}:{Port}")]
    private partial void LogUdpEndpoint(string? ip, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP receive error")]
    private partial void LogUdpReceiveError(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SendAudioToAllPeers: нет зарегистрированных peers")]
    private partial void LogNoPeers();

    [LoggerMessage(Level = LogLevel.Debug, Message = "UDP отправлен → userId={PeerId} endpoint={Endpoint} bytes={Bytes}")]
    private partial void LogUdpSent(int peerId, IPEndPoint endpoint, int bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP send error → peer {PeerId}")]
    private partial void LogUdpSendError(int peerId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AnnounceUdpEndpoint пропущен: callId={CallId} localIp={LocalIp}")]
    private partial void LogAnnouncementSkipped(string? callId, string? localIp);

    [LoggerMessage(Level = LogLevel.Information, Message = "Анонсирован endpoint {Ip}:{Port} → userId={Target}")]
    private partial void LogEndpointAnnounced(string? ip, int port, int target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Получен UDP endpoint от userId={UserId}: {Payload}")]
    private partial void LogEndpointReceived(int userId, string payload);

    [LoggerMessage(Level = LogLevel.Information, Message = "Зарегистрирован endpoint peer userId={UserId} → {Endpoint}")]
    private partial void LogPeerEndpointRegistered(int userId, IPEndPoint endpoint);
    [LoggerMessage(Level = LogLevel.Warning, Message = "ToggleSpeaking SignalR error")]
    private partial void LogSpeakingToggleFailed(Exception? ex);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleanup called, stack: {Stack}")]
    private partial void LogCleanupStarted(string? stack);
    [LoggerMessage(Level = LogLevel.Information, Message = "Выбран endpoint для userId={UserId}: {Selected} из {AllCandidates}")]
    private partial void LogEndpointSelected(int userId, string selected, string allCandidates);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Пропущен endpoint от userId={UserId}: участник не в звонке")]
    private partial void LogEndpointSkippedNotInCall(int userId);
    #endregion
}