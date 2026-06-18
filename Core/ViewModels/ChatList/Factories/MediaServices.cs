using Core.Services.Abstractions;
using Core.Services.Features.Media.Files;

namespace Core.ViewModels.ChatList.Factories;

public sealed class MediaServices(IFileDownloadService fileDownloadService, IFileDownloadStateService fileDownloadStateService,
    IAudioPlayerService audioPlayer, IAudioRecorderService audioRecorder, IPlatformService platformService)
{
    public IFileDownloadService FileDownloadService { get; } = fileDownloadService;
    public IFileDownloadStateService FileDownloadStateService { get; } = fileDownloadStateService;
    public IAudioPlayerService AudioPlayer { get; } = audioPlayer;
    public IAudioRecorderService AudioRecorder { get; } = audioRecorder;
    public IPlatformService PlatformService { get; } = platformService;
}