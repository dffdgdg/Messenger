using Android.Content;
using Core.Services.Call.WebRtc;
using Java.Lang;
using Java.Nio;
using Java.Util;
using Microsoft.Extensions.Logging;
using Org.Webrtc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mobile.Android.Services;

public sealed class WebRtcPeerConnection : IAsyncDisposable,
    PeerConnection.IObserver,
    ISdpObserver
{
    private readonly PeerConnectionFactory _factory;
    private PeerConnection _pc;
    private DataChannel _audioChannel;
    private readonly int _peerId;
    private readonly ILogger _logger;
    private readonly EglBase _eglBase;
    private TaskCompletionSource<string> _sdpTcs;

    public int PeerId => _peerId;
    public bool IsReady => _audioChannel?.State == DataChannel.State.Open;

    public event Action<int> Ready;
    public event Action<int, byte[]> AudioReceived;
    public event Action<string, string> SignalingMessageReady;

    public WebRtcPeerConnection(int peerId, ILogger logger, IceServerConfig iceConfig = null)
    {
        _peerId = peerId;
        _logger = logger;

        var options = PeerConnectionFactory.InitializationOptions
            .InvokeBuilder(Android.App.Application.Context)
            .CreateInitializationOptions();
        PeerConnectionFactory.Initialize(options);

        _eglBase = EglBase.Create();

        _factory = PeerConnectionFactory.InvokeBuilder()
            .SetOptions(new PeerConnectionFactory.Options())
            .SetVideoEncoderFactory(new DefaultVideoEncoderFactory(
                _eglBase.EglBaseContext, true, true))
            .SetVideoDecoderFactory(new DefaultVideoDecoderFactory(
                _eglBase.EglBaseContext))
            .CreatePeerConnectionFactory();

        var rtcConfig = new PeerConnection.RTCConfiguration(GetIceServers(iceConfig));
        rtcConfig.SdpSemantics = PeerConnection.SdpSemantics.UnifiedPlan;
        rtcConfig.ContinualGatheringPolicy =
            PeerConnection.ContinualGatheringPolicy.GatherOnce;
        rtcConfig.KeyType = PeerConnection.KeyType.Ecdsa;

        _pc = _factory.CreatePeerConnection(rtcConfig, this);
    }

    private List<PeerConnection.IceServer> GetIceServers(IceServerConfig config)
    {
        var servers = new List<PeerConnection.IceServer>();

        if (config?.StunUrls != null)
        {
            foreach (var url in config.StunUrls)
            {
                servers.Add(PeerConnection.IceServer.InvokeBuilder(url).CreateIceServer());
            }
        }

        if (config?.Turn != null &&
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() < config.Turn.ExpiresAt)
        {
            foreach (var url in config.Turn.Urls)
            {
                servers.Add(PeerConnection.IceServer.InvokeBuilder(url)
                    .SetUsername(config.Turn.Username)
                    .SetPassword(config.Turn.Credential)
                    .CreateIceServer());
            }
        }

        // Всегда добавляем Google STUN
        servers.Add(PeerConnection.IceServer.InvokeBuilder(
            "stun:stun.l.google.com:19302").CreateIceServer());

        return servers;
    }

    // Реализация PeerConnection.IObserver
    public void OnIceCandidate(IceCandidate candidate)
    {
        if (candidate == null) return;

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            candidate = candidate.Sdp,
            sdpMid = candidate.SdpMid,
            sdpMLineIndex = candidate.SdpMLineIndex
        });

        SignalingMessageReady?.Invoke("webrtc-ice", json);
    }

    public void OnIceCandidatesRemoved(IceCandidate[] candidates) { }

    public void OnSignalingChange(PeerConnection.SignalingState state)
    {
        _logger?.LogDebug($"Peer {_peerId} signaling state: {state}");
    }

    public void OnIceConnectionChange(PeerConnection.IceConnectionState state)
    {
        _logger?.LogDebug($"Peer {_peerId} ICE state: {state}");

        if (state == PeerConnection.IceConnectionState.Failed)
        {
            _logger?.LogError($"Peer {_peerId} ICE failed. Проверьте TURN сервер.");
        }
    }

    public void OnIceConnectionReceivingChange(bool receiving) { }

    public void OnIceGatheringChange(PeerConnection.IceGatheringState state)
    {
        if (state == PeerConnection.IceGatheringState.Complete)
        {
            _sdpTcs?.TrySetResult(
                _pc.LocalDescription?.Description ?? string.Empty);
        }
    }

    public void OnAddStream(MediaStream stream) { }

    public void OnRemoveStream(MediaStream stream) { }

    public void OnDataChannel(DataChannel channel)
    {
        if (channel.Label != "audio") return;
        AttachDataChannel(channel);
    }

    public void OnRenegotiationNeeded() { }

    public void OnAddTrack(MediaStreamTrack track, MediaStream stream) { }

    // Создание Offer (Android)
    public async Task<string> CreateOfferAsync(CancellationToken ct = default)
    {
        var init = new DataChannel.Init
        {
            Ordered = false,
            MaxRetransmits = 0,
            Negotiated = false
        };

        _audioChannel = _pc.CreateDataChannel("audio", init);
        AttachDataChannelEvents(_audioChannel);

        _sdpTcs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pc.CreateOffer(this, new MediaConstraints());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        linked.Token.Register(() => _sdpTcs.TrySetResult(string.Empty));

        return await _sdpTcs.Task;
    }

    // Обработка Offer
    public async Task<string> HandleOfferAsync(string sdpOffer, CancellationToken ct = default)
    {
        _sdpTcs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var sessionDescription = new SessionDescription(
            SessionDescription.Type.Offer, sdpOffer);

        _pc.SetRemoteDescription(this, sessionDescription);
        _pc.CreateAnswer(this, new MediaConstraints());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        linked.Token.Register(() => _sdpTcs.TrySetResult(string.Empty));

        return await _sdpTcs.Task;
    }

    // Обработка Answer
    public Task HandleAnswerAsync(string sdpAnswer)
    {
        var sessionDescription = new SessionDescription(
            SessionDescription.Type.Answer, sdpAnswer);

        _pc.SetRemoteDescription(this, sessionDescription);
        return Task.CompletedTask;
    }

    // Добавление ICE кандидата
    public Task AddIceCandidateAsync(string candidateJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(candidateJson);
            var root = doc.RootElement;

            var candidateStr = root.GetProperty("candidate").GetString();
            var sdpMid = root.GetProperty("sdpMid").GetString();
            var sdpMLineIndex = root.GetProperty("sdpMLineIndex").GetInt32();

            var iceCandidate = new IceCandidate(
                sdpMid, sdpMLineIndex, candidateStr);

            _pc.AddIceCandidate(iceCandidate);
        }
        catch (System.Exception ex)
        {
            _logger?.LogError(ex, $"AddIceCandidate error for peer {_peerId}");
        }

        return Task.CompletedTask;
    }

    // Отправка аудио
    public void SendAudio(byte[] opusData)
    {
        if (!IsReady || _audioChannel == null) return;

        try
        {
            var buffer = ByteBuffer.Wrap(opusData);
            var dataBuffer = new DataChannel.Buffer(buffer, false);
            _audioChannel.Send(dataBuffer);
        }
        catch (System.Exception ex)
        {
            _logger?.LogError(ex, $"SendAudio error peer {_peerId}");
        }
    }

    // Реализация ISdpObserver
    public void OnCreateSuccess(SessionDescription sdp)
    {
        _pc.SetLocalDescription(this, sdp);
    }

    public void OnSetSuccess()
    {
        if (_pc.LocalDescription != null)
        {
            _sdpTcs?.TrySetResult(_pc.LocalDescription.Description);
        }
    }

    public void OnCreateFailure(string error)
    {
        _logger?.LogError($"SDP create failed: {error}");
        _sdpTcs?.TrySetException(new System.Exception(error));
    }

    public void OnSetFailure(string error)
    {
        _logger?.LogError($"SDP set failed: {error}");
    }

    // Вспомогательные методы
    private void AttachDataChannel(DataChannel channel)
    {
        _audioChannel = channel;
        AttachDataChannelEvents(channel);
    }

    private void AttachDataChannelEvents(DataChannel channel)
    {
        channel.RegisterObserver(new DataChannelObserver(this));
    }

    // Observer для DataChannel
    private class DataChannelObserver : Java.Lang.Object, DataChannel.IObserver
    {
        private readonly WebRtcPeerConnection _parent;

        public DataChannelObserver(WebRtcPeerConnection parent)
        {
            _parent = parent;
        }

        public void OnStateChange()
        {
            var state = _parent._audioChannel?.State;

            if (state == DataChannel.State.Open)
            {
                Debug.WriteLine($"[WebRTC] DataChannel открыт peer {_parent._peerId}");
                _parent.Ready?.Invoke(_parent._peerId);
            }
            else if (state == DataChannel.State.Closed)
            {
                Debug.WriteLine($"[WebRTC] DataChannel закрыт peer {_parent._peerId}");
            }
        }

        public void OnMessage(DataChannel.Buffer buffer)
        {
            if (buffer.Data.Remaining() > 0)
            {
                var data = new byte[buffer.Data.Remaining()];
                buffer.Data.Get(data);
                _parent.AudioReceived?.Invoke(_parent._peerId, data);
            }
        }

        public void OnBufferedAmountChange(long previousAmount) { }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _audioChannel?.Close();
            _audioChannel?.Dispose();
            _pc?.Close();
            _pc?.Dispose();
            _factory?.Dispose();
            _eglBase?.Release();
        }
        catch { }
        await ValueTask.CompletedTask;
    }
}