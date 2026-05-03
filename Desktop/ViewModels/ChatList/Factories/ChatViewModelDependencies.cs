using Desktop.Data.Repositories.Abstractions;
using Desktop.ViewModels.Chat;

namespace Desktop.ViewModels.ChatList.Factories;

/// <summary>
/// Набор долгоживущих зависимостей для создания <see cref="ChatViewModel"/>.
/// </summary>
public sealed class ChatViewModelDependencies(ChatCoreServices core, MediaServices media, CallServices calls, CacheServices cache)
{
    public ChatCoreServices Core { get; } = core;
    public MediaServices Media { get; } = media;
    public CallServices Calls { get; } = calls;
    public CacheServices Cache { get; } = cache;
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