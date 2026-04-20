using MessengerShared.DTO.Call;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Call;

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
    private string? _localIp;
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

        try
        {
            _audio.Start();
        }
        catch (Exception ex)
        {
            LogAudioUnavailable(ex);
        }

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

    public async Task DeclineCallAsync(string callId)
        => await _hub.DeclineCallAsync(callId);

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

    private void InitUdp()
    {
        _localIp = GetLocalIpAddress();
        _udpClient = new UdpClient(0);
        _localUdpPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

        LogUdpEndpoint(_localIp, _localUdpPort);

        _receiveCts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

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
        if (_session.UserId is not int userId) return;

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
                LogUdpSent(peerId, endpoint, packet.Length);
            }
            catch (Exception ex)
            {
                LogUdpSendError(peerId, ex);
            }
        }
    }

    private async Task AnnounceUdpEndpointAsync(int targetUserId)
    {
        if (_activeCallId == null || _localIp == null)
        {
            LogAnnouncementSkipped(_activeCallId, _localIp);
            return;
        }

        await _hub.SendSignalAsync(new WebRtcSignalDto
        {
            CallId = _activeCallId,
            TargetUserId = targetUserId,
            Type = "udp-endpoint",
            Payload = $"{_localIp}:{_localUdpPort}"
        });

        LogEndpointAnnounced(_localIp, _localUdpPort, targetUserId);
    }

    private void HandleUdpEndpointSignal(WebRtcSignalDto signal)
    {
        LogEndpointReceived(signal.FromUserId, signal.Payload);

        var parts = signal.Payload.Split(':');
        if (parts.Length != 2) return;
        if (!IPAddress.TryParse(parts[0], out var ip)) return;
        if (!int.TryParse(parts[1], out var port)) return;

        var endpoint = new IPEndPoint(ip, port);
        _peerEndpoints[signal.FromUserId] = endpoint;
        _audio.AddParticipant(signal.FromUserId);

        LogPeerEndpointRegistered(signal.FromUserId, endpoint);

        if (_endpointAnnounced.TryAdd(signal.FromUserId, true))
            _ = AnnounceUdpEndpointAsync(signal.FromUserId);
    }

    private static string GetLocalIpAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());

        foreach (var addr in host.AddressList)
        {
            if (addr.AddressFamily == AddressFamily.InterNetwork)
                Debug.WriteLine($"[CallService] Найден IP: {addr}");
        }

        try
        {
            var serverHost = new Uri(App.ApiUrl).Host;
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(serverHost, 65530);
            var ip = ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
            Debug.WriteLine($"[CallService] Выбран IP через маршрут к серверу: {ip}");
            return ip;
        }
        catch
        {
            foreach (var addr in host.AddressList)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                {
                    Debug.WriteLine($"[CallService] Fallback IP: {addr}");
                    return addr.ToString();
                }
            }
            return "127.0.0.1";
        }
    }

    private void SubscribeHubEvents()
    {
        _hub.CallStateUpdated += OnCallStateUpdated;
        _hub.CallParticipantJoined += OnParticipantJoined;
        _hub.CallParticipantLeft += OnParticipantLeft;
        _hub.CallEnded += OnCallEndedRemotely;
        _hub.SignalReceived += OnSignalReceived;
    }

    private void UnsubscribeHubEvents()
    {
        _hub.CallStateUpdated -= OnCallStateUpdated;
        _hub.CallParticipantJoined -= OnParticipantJoined;
        _hub.CallParticipantLeft -= OnParticipantLeft;
        _hub.CallEnded -= OnCallEndedRemotely;
        _hub.SignalReceived -= OnSignalReceived;
    }

    private void OnCallStateUpdated(CallStateDto state)
    {
        if (_activeCallId == null)
        {
            _activeCallId = state.CallId;
            CallStarted?.Invoke();
        }

        var myUserId = _session.UserId ?? 0;
        foreach (var p in state.Participants)
        {
            if (p.UserId == myUserId) continue;
            _audio.AddParticipant(p.UserId);
            if (_endpointAnnounced.TryAdd(p.UserId, true))
                _ = AnnounceUdpEndpointAsync(p.UserId);
        }
    }

    private void OnParticipantJoined(string callId, CallParticipantDto participant)
    {
        if (callId != _activeCallId) return;
        if (_session.UserId is int myId && participant.UserId == myId) return;

        _audio.AddParticipant(participant.UserId);
        if (_endpointAnnounced.TryAdd(participant.UserId, true))
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
            HandleUdpEndpointSignal(signal);
    }

    private void Cleanup()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        _udpClient?.Dispose();
        _udpClient = null;

        _peerEndpoints.Clear();
        _endpointAnnounced.Clear();
        _localIp = null;

        _audio.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeHubEvents();
        _audio.OnEncodedFrame -= SendAudioToAllPeers;

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
    #endregion
}