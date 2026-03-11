using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.Services.UI;
using System;
using System.Threading;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Разделяемый контекст чата.
/// Содержит идентификаторы, зависимости, общие коллекции
/// и события координации между handlers.
/// Не содержит бизнес-логики.
/// </summary>
public sealed class ChatContext(int chatId, int currentUserId, IApiClientService api,
    IGlobalHubConnection hub,INotificationService notifications, IChatNotificationApiService notificationApi,
    IFileDownloadService fileDownload, ILocalCacheService? cache) : ObservableObject, IDisposable
{
    public int ChatId { get; } = chatId;
    public int CurrentUserId { get; } = currentUserId;

    public ChatDto? Chat { get; set; }

    private ObservableCollection<UserDto> _members = [];

    public ObservableCollection<UserDto> Members
    {
        get => _members;
        set => SetProperty(ref _members, value);
    }

    public IApiClientService Api { get; } = api;
    public IGlobalHubConnection Hub { get; } = hub;
    public INotificationService Notifications { get; } = notifications;
    public IChatNotificationApiService NotificationApi { get; } = notificationApi;
    public IFileDownloadService FileDownload { get; } = fileDownload;
    public ILocalCacheService? Cache { get; } = cache;

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;
    public event Action<int, bool>? ScrollToIndexRequested;
    public event Action? ScrollToBottomRequested;

    public void RequestScrollToMessage(MessageViewModel msg, bool highlight = false)
        => ScrollToMessageRequested?.Invoke(msg, highlight);

    public void RequestScrollToIndex(int index, bool highlight = false)
        => ScrollToIndexRequested?.Invoke(index, highlight);

    public void RequestScrollToBottom()
        => ScrollToBottomRequested?.Invoke();

    public event Action? CompositionModeReset;

    public void ResetCompositionModes()
        => CompositionModeReset?.Invoke();

    public bool IsDisposed { get; private set; }

    private CancellationTokenSource? _lifetimeCts = new();
    public CancellationToken LifetimeToken
        => _lifetimeCts?.Token ?? CancellationToken.None;

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        _lifetimeCts?.Cancel();
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
    }
}
