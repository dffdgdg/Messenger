using Core.Services.Features.Call.WebRtc;
using Microsoft.Extensions.Logging;

namespace Desktop.Shared.Services.Platform;

public class DesktopWebRtcFactory(ILogger<WebRtcPeerConnection> logger) : IWebRtcPeerConnectionFactory
{
    public IWebRtcPeerConnection Create(int peerId, IceServerConfig? iceConfig)
        => new WebRtcPeerConnection(peerId, logger, iceConfig);
}