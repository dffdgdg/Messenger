// Desktop/Services/Features/Call/WebRtc/WebRtcPeerConnection.cs

using Microsoft.Extensions.Logging;
using Shared.Dto.Call;
using System.Diagnostics;

#if !ANDROID
using SIPSorcery.Net;
#endif

namespace Core.Services.Features.Call.WebRtc;

/// <summary>
/// Конфигурация ICE серверов — общая для всех платформ.
/// Вынесена из класса чтобы быть доступной под Android.
/// </summary>
public sealed class IceServerConfig
{
    public string[]? StunUrls { get; init; }
    public TurnCredentials? Turn { get; init; }
    public bool TurnOnly { get; init; } = false;

    public static IceServerConfig FromRelayEndpoint(RelayEndpointInfo info)
    {
        return new IceServerConfig
        {
            Turn = info.Turn,
            StunUrls = info.Turn == null
                ? ["stun:stun.l.google.com:19302"]
                : null
        };
    }
}

#if !ANDROID

public sealed class WebRtcPeerConnection : IAsyncDisposable
{
    private readonly RTCPeerConnection _pc;
    private RTCDataChannel? _audioChannel;
    private readonly int _peerId;
    private readonly ILogger _logger;

    public int PeerId => _peerId;
    public bool IsReady => _audioChannel?.readyState == RTCDataChannelState.open;

    public event Action<int>? Ready;
    public event Action<int, byte[]>? AudioReceived;
    public event Action<string, string>? SignalingMessageReady;

    public WebRtcPeerConnection(int peerId, ILogger logger, IceServerConfig? iceConfig = null)
    {
        _peerId = peerId;
        _logger = logger;

        var iceServers = BuildIceServers(iceConfig);

        var config = new RTCConfiguration
        {
            iceServers = iceServers,
            iceTransportPolicy = iceConfig?.TurnOnly == true
                ? RTCIceTransportPolicy.relay
                : RTCIceTransportPolicy.all
        };

        _pc = new RTCPeerConnection(config);
        SetupPeerConnection();
    }

    private static List<RTCIceServer> BuildIceServers(IceServerConfig? config)
    {
        var servers = new List<RTCIceServer>();

        if (config?.Turn != null)
        {
            var turn = config.Turn;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (turn.ExpiresAt > now)
            {
                foreach (var url in turn.Urls)
                {
                    servers.Add(new RTCIceServer
                    {
                        urls = url,
                        username = turn.Username,
                        credential = turn.Credential,
                        credentialType = RTCIceCredentialType.password
                    });
                }
            }
            else
            {
                Debug.WriteLine("[WebRTC] TURN credentials истекли, fallback на STUN");
            }
        }

        if (config?.StunUrls != null)
        {
            foreach (var url in config.StunUrls)
                servers.Add(new RTCIceServer { urls = url });
        }

        if (servers.Count == 0)
        {
            servers.Add(new RTCIceServer
            {
                urls = "stun:stun.l.google.com:19302"
            });
        }

        return servers;
    }

    private void SetupPeerConnection()
    {
        _pc.onicecandidate += candidate =>
        {
            if (candidate == null) return;

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                candidate.candidate,
                candidate.sdpMid,
                candidate.sdpMLineIndex
            });

            SignalingMessageReady?.Invoke("webrtc-ice", json);
        };

        _pc.onconnectionstatechange += state =>
        {
            Debug.WriteLine($"[WebRTC] Peer {_peerId} connection state: {state}");
            if (state == RTCPeerConnectionState.failed)
                Debug.WriteLine($"[WebRTC] Peer {_peerId} — ICE failed. " +
                    "Проверь TURN credentials и доступность сервера.");
        };

        _pc.oniceconnectionstatechange += state =>
            Debug.WriteLine($"[WebRTC] Peer {_peerId} ICE state: {state}");

        _pc.ondatachannel += channel =>
        {
            if (channel.label != "audio") return;
            AttachDataChannel(channel);
        };
    }

    public async Task<string> CreateOfferAsync(CancellationToken ct = default)
    {
        var dcInit = new RTCDataChannelInit
        {
            ordered = false,
            maxRetransmits = 0
        };

        _audioChannel = await _pc.createDataChannel("audio", dcInit);
        AttachDataChannelEvents(_audioChannel);

        var offer = _pc.createOffer();
        _pc.setLocalDescription(offer);

        return await WaitForIceGatheringAsync(ct);
    }

    public async Task<string> HandleOfferAsync(string sdpOffer, CancellationToken ct = default)
    {
        var offerInit = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = sdpOffer
        };

        var setResult = _pc.setRemoteDescription(offerInit);
        if (setResult != SetDescriptionResultEnum.OK)
            throw new InvalidOperationException($"setRemoteDescription failed: {setResult}");

        var answer = _pc.createAnswer();
        _pc.setLocalDescription(answer);

        return await WaitForIceGatheringAsync(ct);
    }

    public Task HandleAnswerAsync(string sdpAnswer)
    {
        var result = _pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = sdpAnswer
        });

        if (result != SetDescriptionResultEnum.OK)
            Debug.WriteLine($"[WebRTC] setRemoteDescription(answer) failed: {result}");

        return Task.CompletedTask;
    }

    public Task AddIceCandidateAsync(string candidateJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(candidateJson);
            var root = doc.RootElement;

            var candidateStr = root.GetProperty("candidate").GetString() ?? string.Empty;
            var sdpMid = root.TryGetProperty("sdpMid", out var mid) ? mid.GetString() : null;

            ushort sdpMLineIndex = 0;
            if (root.TryGetProperty("sdpMLineIndex", out var idx))
                sdpMLineIndex = idx.GetUInt16();

            _pc.addIceCandidate(new RTCIceCandidateInit
            {
                candidate = candidateStr,
                sdpMid = sdpMid,
                sdpMLineIndex = sdpMLineIndex
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRTC] AddIceCandidate error: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void SendAudio(byte[] opusData)
    {
        if (!IsReady) return;
        try { _audioChannel!.send(opusData); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRTC] SendAudio error peer {_peerId}: {ex.Message}");
        }
    }

    private void AttachDataChannel(RTCDataChannel channel)
    {
        _audioChannel = channel;
        AttachDataChannelEvents(channel);
    }

    private void AttachDataChannelEvents(RTCDataChannel channel)
    {
        channel.onopen += () =>
        {
            Debug.WriteLine($"[WebRTC] DataChannel открыт peer {_peerId}");
            Ready?.Invoke(_peerId);
        };

        channel.onmessage += (_, __, data) =>
            AudioReceived?.Invoke(_peerId, data);

        channel.onclose += () =>
            Debug.WriteLine($"[WebRTC] DataChannel закрыт peer {_peerId}");
    }

    private async Task<string> WaitForIceGatheringAsync(CancellationToken ct)
    {
        if (_pc.iceGatheringState == RTCIceGatheringState.complete)
            return _pc.localDescription.sdp.ToString();

        var tcs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pc.onicegatheringstatechange += state =>
        {
            if (state == RTCIceGatheringState.complete)
                tcs.TrySetResult(_pc.localDescription.sdp.ToString());
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        linked.Token.Register(() =>
        {
            var sdp = _pc.localDescription?.sdp?.ToString() ?? string.Empty;
            tcs.TrySetResult(sdp);
        });

        return await tcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        try { _pc.close(); } catch { }
        try { _pc.Dispose(); } catch { }
        await ValueTask.CompletedTask;
    }
}

#else

/// <summary>
/// Заглушка под Android — WebRTC через SIPSorcery не поддерживается.
/// Под Android будет использоваться нативный WebRTC или другой механизм.
/// </summary>
public sealed class WebRtcPeerConnection : IAsyncDisposable
{
    public int PeerId { get; }
    public bool IsReady => false;

    public event Action<int>? Ready;
    public event Action<int, byte[]>? AudioReceived;
    public event Action<string, string>? SignalingMessageReady;

    public WebRtcPeerConnection(int peerId, ILogger logger, IceServerConfig? iceConfig = null)
    {
        PeerId = peerId;
        Debug.WriteLine($"[WebRTC] Android stub — peer {peerId}");
    }

    public Task<string> CreateOfferAsync(CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task<string> HandleOfferAsync(string sdpOffer, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task HandleAnswerAsync(string sdpAnswer)
        => Task.CompletedTask;

    public Task AddIceCandidateAsync(string candidateJson)
        => Task.CompletedTask;

    public void SendAudio(byte[] opusData) { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

#endif