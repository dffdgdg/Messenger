using Core.Services.Call.WebRtc;

namespace Core.Services.Call.Abstractions;

public interface IWebRtcPeerConnectionFactory
{
    IWebRtcPeerConnection Create(int peerId, IceServerConfig? iceConfig);
}
