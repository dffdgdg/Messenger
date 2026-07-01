using Core.Services.Call.Abstractions;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Call;

namespace Core.Services.Call.WebRtc;

public sealed class WebRtcManager(int localUserId, string callId, ILogger logger, IWebRtcPeerConnectionFactory peerFactory,
    IceServerConfig? iceConfig = null) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, IWebRtcPeerConnection> _peers = new();

    public event Action<int>? PeerReady;
    public event Action<int, byte[]>? AudioReceived;
    public event Action<SignalDto>? SignalingReady;

    public async Task InitiateAsync(int peerId, CancellationToken ct = default)
    {
        if (_peers.ContainsKey(peerId)) return;

        var peer = CreatePeer(peerId);
        _peers[peerId] = peer;

        var sdp = await peer.CreateOfferAsync(ct);

        SignalingReady?.Invoke(new SignalDto
        {
            CallId = callId,
            FromUserId = localUserId,
            TargetUserId = peerId,
            Type = "webrtc-offer",
            Payload = sdp
        });
    }

    public async Task HandleSignalAsync(SignalDto signal, CancellationToken ct = default)
    {
        var peerId = signal.FromUserId;

        switch (signal.Type)
        {
            case "webrtc-offer":
                await HandleOfferAsync(peerId, signal.Payload, ct);
                break;

            case "webrtc-answer":
                if (_peers.TryGetValue(peerId, out var answerPeer))
                    await answerPeer.HandleAnswerAsync(signal.Payload);
                break;

            case "webrtc-ice":
                if (_peers.TryGetValue(peerId, out var icePeer))
                    await icePeer.AddIceCandidateAsync(signal.Payload);
                break;
        }
    }

    public void SendAudioToAll(byte[] opusData)
    {
        foreach (var (_, peer) in _peers)
            if (peer.IsReady) peer.SendAudio(opusData);
    }

    public void SendAudioToPeer(int peerId, byte[] opusData)
    {
        if (_peers.TryGetValue(peerId, out var peer) && peer.IsReady)
            peer.SendAudio(opusData);
    }

    public async Task RemovePeerAsync(int peerId)
    {
        if (_peers.TryRemove(peerId, out var peer))
            await peer.DisposeAsync();
    }

    public bool HasPeer(int peerId) => _peers.ContainsKey(peerId);

    /// <summary>
    /// Обновить ICE конфигурацию — применится к следующим создаваемым peer-ам.
    /// </summary>
    public void UpdateIceConfig(IceServerConfig newConfig) => iceConfig = newConfig;

    private async Task HandleOfferAsync(int peerId, string sdpOffer, CancellationToken ct)
    {
        if (_peers.TryRemove(peerId, out var old))
            await old.DisposeAsync();

        var peer = CreatePeer(peerId);
        _peers[peerId] = peer;

        var answerSdp = await peer.HandleOfferAsync(sdpOffer, ct);

        SignalingReady?.Invoke(new SignalDto
        {
            CallId = callId,
            FromUserId = localUserId,
            TargetUserId = peerId,
            Type = "webrtc-answer",
            Payload = answerSdp
        });
    }

    private IWebRtcPeerConnection CreatePeer(int peerId)
    {
        var peer = peerFactory.Create(peerId, iceConfig);

        peer.Ready += id => PeerReady?.Invoke(id);
        peer.AudioReceived += (id, data) => AudioReceived?.Invoke(id, data);
        peer.SignalingMessageReady += (type, payload) =>
        {
            SignalingReady?.Invoke(new SignalDto
            {
                CallId = callId,
                FromUserId = localUserId,
                TargetUserId = peerId,
                Type = type,
                Payload = payload
            });
        };

        return peer;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, peer) in _peers)
            try { await peer.DisposeAsync(); } catch { }
        _peers.Clear();
    }
}