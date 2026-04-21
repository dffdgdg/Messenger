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
public sealed class ChatViewModelDependencies(ChatCoreServices core, ChatMediaServices media, ChatCallServices calls, ChatCacheServices cache)
{
    public IApiClientService ApiClient { get; } = core.ApiClient;
    public IAuthManager AuthManager { get; } = core.AuthManager;
    public IChatInfoPanelStateStore ChatInfoPanelStateStore { get; } = core.ChatInfoPanelStateStore;
    public INotificationService NotificationService { get; } = core.NotificationService;
    public IChatNotificationApiService NotificationApiService { get; } = core.NotificationApiService;
    public IDialogService DialogService { get; } = core.DialogService;
    public IGlobalHubConnection GlobalHub { get; } = core.GlobalHub;
    public IFileDownloadService FileDownloadService { get; } = media.FileDownloadService;
    public IPlatformService PlatformService { get; } = media.PlatformService;
    public ICallService CallService { get; } = calls.CallService;
    public ICallHubConnection CallHub { get; } = calls.CallHub;
    public ILocalCacheService CacheService { get; } = cache.CacheService;
    public IAudioPlayerService AudioPlayer { get; } = media.AudioPlayer;
    public IAudioRecorderService AudioRecorder { get; } = media.AudioRecorder;
}

/// <summary>
/// Базовые сервисы чата: API, авторизация, UI-диалоги и SignalR.
/// </summary>
public sealed class ChatCoreServices(IApiClientService apiClient, IAuthManager authManager, IChatInfoPanelStateStore chatInfoPanelStateStore, INotificationService notificationService,
    IChatNotificationApiService notificationApiService, IDialogService dialogService, IGlobalHubConnection globalHub)
{
    public IApiClientService ApiClient { get; } = apiClient;
    public IAuthManager AuthManager { get; } = authManager;
    public IChatInfoPanelStateStore ChatInfoPanelStateStore { get; } = chatInfoPanelStateStore;
    public INotificationService NotificationService { get; } = notificationService;
    public IChatNotificationApiService NotificationApiService { get; } = notificationApiService;
    public IDialogService DialogService { get; } = dialogService;
    public IGlobalHubConnection GlobalHub { get; } = globalHub;
}

/// <summary>
/// Сервисы чата для файлов и воспроизведения медиа.
/// </summary>
public sealed class ChatMediaServices(IFileDownloadService fileDownloadService, IPlatformService platformService, IAudioPlayerService audioPlayer, IAudioRecorderService audioRecorder)
{
    public IFileDownloadService FileDownloadService { get; } = fileDownloadService;
    public IPlatformService PlatformService { get; } = platformService;
    public IAudioPlayerService AudioPlayer { get; } = audioPlayer;
    public IAudioRecorderService AudioRecorder { get; } = audioRecorder;
}

/// <summary>
/// Сервисы голосовых звонков внутри чата.
/// </summary>
public sealed class ChatCallServices(ICallService callService, ICallHubConnection callHub)
{
    public ICallService CallService { get; } = callService;
    public ICallHubConnection CallHub { get; } = callHub;
}

/// <summary>
/// Сервисы локального кэширования чата.
/// </summary>
public sealed class ChatCacheServices(ILocalCacheService cacheService)
{
    public ILocalCacheService CacheService { get; } = cacheService;
}