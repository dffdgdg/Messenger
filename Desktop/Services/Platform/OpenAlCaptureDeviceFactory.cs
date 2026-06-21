using Core.Services.Features.Media.Abstractions;

namespace Desktop.Services.Platform;

public sealed class OpenAlCaptureDeviceFactory : IAudioCaptureDeviceFactory
{
    public IAudioCaptureDevice Create() => new OpenAlCaptureDevice();
}