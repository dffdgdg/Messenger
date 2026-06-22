using Core.Services.Features.Call.WebRtc;

namespace Core.Services.Abstractions;

public interface IWebRtcPeerConnectionFactory
{
    IWebRtcPeerConnection Create(int peerId, IceServerConfig? iceConfig);
}
