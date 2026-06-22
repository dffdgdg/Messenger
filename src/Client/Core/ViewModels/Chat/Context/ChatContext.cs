using Core.Data.Repositories.Abstractions;
using Core.Services.Features.Media.Files;
using Core.ViewModels.Chat.Navigation;
using Core.ViewModels.ChatList.Factories;

namespace Core.ViewModels.Chat.Context;

public sealed class ChatContext : ObservableObject, IDisposable
{
    public event Action<ObservableCollection<UserDto>, ObservableCollection<UserDto>>? MembersReplaced;
    public event Action<MessageDto>? MessagePinStateChanged;
    private ObservableCollection<UserDto> _members = [];
    public ChatRole? CurrentUserRole { get; set; }
    public bool IsSystemAdmin { get; set; }
    public IChatNavigator? Navigator { get; init; }
    public Action<MessageDto>? RequestIncrementCounters { get; set; }
    public Action? RequestRefreshCounters { get; set; }

    public ObservableCollection<UserDto> Members
    {
        get => _members;
        set
        {
            var old = _members;
            _members = value;
            MembersReplaced?.Invoke(old, value);
            OnPropertyChanged();
        }
    }

    public int ChatId { get; }
    public int CurrentUserId { get; }
    public ChatDto? Chat
    {
        get => chat; set => SetProperty(ref chat, value);
    }

    public IApiClientService Api { get; }
    public IDialogService Dialogs { get; }
    public IGlobalHubConnection Hub { get; }
    public INotificationService Notifications { get; }
    public IChatNotificationApiService NotificationApi { get; }
    public IFileDownloadService FileDownload { get; }
    public IFileDownloadStateService? FileDownloadState { get; }
    public ILocalCacheService? Cache { get; }

    public ChatContext(int chatId, int currentUserId, ChatCoreServices core, MediaServices media, CacheServices cache)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(cache);

        ChatId = chatId;
        CurrentUserId = currentUserId;
        Api = core.ApiClient;
        Dialogs = core.DialogService;
        Hub = core.GlobalHub;
        Notifications = core.NotificationService;
        NotificationApi = core.NotificationApiService;
        FileDownload = media.FileDownloadService;
        FileDownloadState = media.FileDownloadStateService;
        Cache = cache.CacheService;
    }

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;
    public event Action<int, bool>? ScrollToIndexRequested;
    public event Action? ScrollToBottomRequested;
    public void RequestScrollToMessage(MessageViewModel msg, bool highlight = false) => ScrollToMessageRequested?.Invoke(msg, highlight);
    public void RequestScrollToIndex(int index, bool highlight = false) => ScrollToIndexRequested?.Invoke(index, highlight);
    public void RequestScrollToBottom() => ScrollToBottomRequested?.Invoke();

    public event Action? CompositionModeReset;
    public void ResetCompositionModes() => CompositionModeReset?.Invoke();
    public void RaisePinStateChanged(MessageDto dto)
        => MessagePinStateChanged?.Invoke(dto);
    public bool IsDisposed { get; private set; }

    private CancellationTokenSource? _lifetimeCts = new();
    private ChatDto? chat;

    public CancellationToken LifetimeToken => _lifetimeCts?.Token ?? CancellationToken.None;

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        _lifetimeCts?.Cancel();
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
    }
}