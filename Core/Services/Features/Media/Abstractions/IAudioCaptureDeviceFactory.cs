namespace Core.Services.Features.Media.Abstractions;

public interface IAudioCaptureDeviceFactory
{
    /// <summary>Создаёт новый независимый экземпляр capture device.</summary>
    IAudioCaptureDevice Create();
}