namespace Core.Services.Abstractions;

public interface IWebRtcPeerConnection : IAsyncDisposable
{
    int PeerId { get; }
    bool IsReady { get; }

    event Action<int> Ready;
    event Action<int, byte[]> AudioReceived;
    event Action<string, string> SignalingMessageReady;

    Task<string> CreateOfferAsync(CancellationToken ct = default);
    Task<string> HandleOfferAsync(string sdpOffer, CancellationToken ct = default);
    Task HandleAnswerAsync(string sdpAnswer);
    Task AddIceCandidateAsync(string candidateJson);
    void SendAudio(byte[] opusData);
}