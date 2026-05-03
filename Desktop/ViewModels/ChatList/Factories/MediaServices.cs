namespace Desktop.ViewModels.ChatList.Factories;

/// <summary>
/// Сервисы чата для файлов и воспроизведения медиа.
/// </summary>
public sealed class MediaServices(IFileDownloadService fileDownloadService, IPlatformService platformService, IAudioPlayerService audioPlayer, IAudioRecorderService audioRecorder)
{
    public IFileDownloadService FileDownloadService { get; } = fileDownloadService;
    public IPlatformService PlatformService { get; } = platformService;
    public IAudioPlayerService AudioPlayer { get; } = audioPlayer;
    public IAudioRecorderService AudioRecorder { get; } = audioRecorder;
}