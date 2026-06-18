#if !ANDROID
using Core.Services.Features.Media.Abstractions;

namespace Core.Services.Features.Media.Audio.Platform.Desktop;

public sealed class OpenAlCaptureDeviceFactory : IAudioCaptureDeviceFactory
{
    public IAudioCaptureDevice Create() => new OpenAlCaptureDevice();
}
#endif