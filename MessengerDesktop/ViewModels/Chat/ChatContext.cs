using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Factories;
using System;
using System.Threading;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Разделяемый контекст чата.
/// Содержит идентификаторы, зависимости, общие коллекции
/// и события координации между handlers.
/// Не содержит бизнес-логики.
/// </summary>
public sealed class ChatContext : ObservableObject, IDisposable
{
    public int ChatId { get; }
    public int CurrentUserId { get; }

    private ChatDto? _chat;
    public ChatDto? Chat
    {
        get => _chat;
        set => SetProperty(ref _chat, value);
    }

    private ObservableCollection<UserDto> _members = [];

    public ObservableCollection<UserDto> Members
    {
        get => _members;
        set => SetProperty(ref _members, value);
    }

    public IApiClientService Api { get; }
    public IDialogService Dialogs { get; }
    public IGlobalHubConnection Hub { get; }
    public INotificationService Notifications { get; }
    public IChatNotificationApiService NotificationApi { get; }
    public IFileDownloadService FileDownload { get; }
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

    public bool IsDisposed { get; private set; }

    private CancellationTokenSource? _lifetimeCts = new();
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
