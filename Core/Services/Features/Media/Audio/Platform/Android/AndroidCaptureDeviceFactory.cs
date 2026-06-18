using Core.Services.Features.Media.Abstractions;
using Core.Services.Features.Media.Audio.Platform.Android;
namespace Core.Services.Features.Media.Audio.Platform.Android;

public sealed class AndroidCaptureDeviceFactory : IAudioCaptureDeviceFactory
{
    public IAudioCaptureDevice Create() => new AndroidCaptureDevice();
}