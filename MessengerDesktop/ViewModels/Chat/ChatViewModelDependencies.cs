using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.Audio;
using MessengerDesktop.Services.Call;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.Services.UI;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Набор долгоживущих зависимостей для создания <see cref="ChatViewModel"/>.
/// </summary>
public sealed class ChatViewModelDependencies(
    IApiClientService apiClient,
    IAuthManager authManager,
    IChatInfoPanelStateStore chatInfoPanelStateStore,
    INotificationService notificationService,
    IChatNotificationApiService notificationApiService,
    IDialogService dialogService,
    IGlobalHubConnection globalHub,
    IFileDownloadService fileDownloadService,
    IPlatformService platformService,
    ICallService callService,
    ICallHubConnection callHub,
    ILocalCacheService cacheService,
    IAudioPlayerService audioPlayer)
{
    public IApiClientService ApiClient { get; } = apiClient;
    public IAuthManager AuthManager { get; } = authManager;
    public IChatInfoPanelStateStore ChatInfoPanelStateStore { get; } = chatInfoPanelStateStore;
    public INotificationService NotificationService { get; } = notificationService;
    public IChatNotificationApiService NotificationApiService { get; } = notificationApiService;
    public IDialogService DialogService { get; } = dialogService;
    public IGlobalHubConnection GlobalHub { get; } = globalHub;
    public IFileDownloadService FileDownloadService { get; } = fileDownloadService;
    public IPlatformService PlatformService { get; } = platformService;
    public ICallService CallService { get; } = callService;
    public ICallHubConnection CallHub { get; } = callHub;
    public ILocalCacheService CacheService { get; } = cacheService;
    public IAudioPlayerService AudioPlayer { get; } = audioPlayer;
}
