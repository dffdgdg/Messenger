using Core.Services.Media.Abstractions;
using Desktop.Shared.Services.Platform;

namespace Desktop.Shared.Services.Platform;

public sealed class OpenAlCaptureDeviceFactory : IAudioCaptureDeviceFactory
{
    public IAudioCaptureDevice Create() => new OpenAlCaptureDevice();
}