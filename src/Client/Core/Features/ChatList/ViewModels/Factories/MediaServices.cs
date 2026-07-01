using Core.Services.Media.Abstractions;
using Core.Services.Media.Files;
using Core.Services.Platform.Abstractions;

namespace Core.Features.ChatList.ViewModels.Factories;

public sealed class MediaServices(IFileDownloadService fileDownloadService, IFileDownloadStateService fileDownloadStateService,
    IAudioPlayerService audioPlayer, IAudioRecorderService audioRecorder, IPlatformService platformService,
    IDownloadManager downloadManager)
{
    public IFileDownloadService FileDownloadService { get; } = fileDownloadService;
    public IFileDownloadStateService FileDownloadStateService { get; } = fileDownloadStateService;
    public IAudioPlayerService AudioPlayer { get; } = audioPlayer;
    public IAudioRecorderService AudioRecorder { get; } = audioRecorder;
    public IPlatformService PlatformService { get; } = platformService;
    public IDownloadManager DownloadManager { get; } = downloadManager;
}