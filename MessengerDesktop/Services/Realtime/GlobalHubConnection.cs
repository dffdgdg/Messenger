using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.Storage;
using MessengerDesktop.Services.UI;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Realtime;

public interface IGlobalHubConnection : IAsyncDisposable, IDisposable
{
    event Action<NotificationDto>? NotificationReceived;
    event Action<int, bool>? UserStatusChanged;
    event Action<int, int>? UnreadCountChanged;
    event Action<int>? TotalUnreadChanged;
    event Action<MessageDto>? MessageReceivedGlobally;
    event Action<MessageDto>? MessageUpdatedGlobally;
    event Action<int, int>? MessageDeletedGlobally;
    event Action<UserDto>? UserProfileUpdated;
    event Action<int, int>? UserTyping;
    event Action<int, int, int?, DateTime?>? MessageRead;
    event Action<int, UserDto>? MemberJoined;
    event Action<int, int>? MemberLeft;
    event Action? Reconnected;

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    void SetCurrentChat(int? chatId);
    Task<AllUnreadCountsDto?> GetUnreadCountsAsync();
    Task MarkChatAsReadAsync(int chatId);
    int GetUnreadCount(int chatId);
    int GetTotalUnread();
    Task<ChatReadInfoDto?> GetReadInfoAsync(int chatId);
    Task MarkMessageAsReadAsync(int chatId, int messageId);
    Task SendTypingAsync(int chatId);
}

public sealed class GlobalHubConnection(
    IAuthManager authManager,
    INotificationService notificationService,
    INavigationService navigationService,
    ISettingsService settingsService,
    ILocalCacheService cacheService) : IGlobalHubConnection
{
    private const int NoChatId = -1;
    private const int ContentPreviewMaxLength = 100;
    private const string PreviewEllipsis = "...";
    private const string NotificationTypePoll = "poll";

    private readonly IAuthManager _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
    private readonly INotificationService _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly ILocalCacheService _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

    private HubConnection? _hubConnection;
    private readonly List<IDisposable> _hubSubscriptions = [];

    private int _disposed;
    private int _connectState;

    private volatile int _openChatId = NoChatId;

    private int _lastSentReadMessageId;
    private DateTime _lastSentReadTime = DateTime.MinValue;
    private DateTime _lastSentTypingTime = DateTime.MinValue;

    private readonly Dictionary<int, int> _unreadCounts = [];
    private int _totalUnread;
    private readonly Lock _unreadLock = new();

    #region Events

    public event Action<NotificationDto>? NotificationReceived;
    public event Action<int, bool>? UserStatusChanged;
    public event Action<int, int>? UnreadCountChanged;
    public event Action<int>? TotalUnreadChanged;
    public event Action<MessageDto>? MessageReceivedGlobally;
    public event Action<MessageDto>? MessageUpdatedGlobally;
    public event Action<int, int>? MessageDeletedGlobally;
    public event Action<UserDto>? UserProfileUpdated;
    public event Action<int, int>? UserTyping;
    public event Action<int, int, int?, DateTime?>? MessageRead;
    public event Action<int, UserDto>? MemberJoined;
    public event Action<int, int>? MemberLeft;
    public event Action? Reconnected;

    #endregion

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public int GetUnreadCount(int chatId)
    {
        lock (_unreadLock)
            return _unreadCounts.GetValueOrDefault(chatId, 0);
    }

    public int GetTotalUnread()
    {
        lock (_unreadLock)
            return _totalUnread;
    }

    #region Connection

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _connectState, 1, 0) != 0)
            return;

        try
        {
            _hubConnection = new HubConnectionBuilder().WithUrl($"{App.ApiUrl}chatHub", options => options.AccessTokenProvider =
            () => Task.FromResult(_authManager.Session.Token)).WithAutomaticReconnect().Build();

            SubscribeHubEvents();

            _hubConnection.Reconnecting += OnHubReconnecting;
            _hubConnection.Reconnected += OnHubReconnectedInternal;

            await _hubConnection.StartAsync(ct);
            Debug.WriteLine("[GlobalHub] Connected successfully");

            await LoadUnreadCountsAsync();
        }
        catch
        {
            Volatile.Write(ref _connectState, 0);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection is null) return;

        try
        {
            await _hubConnection.StopAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] Disconnect error: {ex.Message}");
        }
    }

    public void SetCurrentChat(int? chatId)
    {
        _openChatId = chatId ?? NoChatId;
        _lastSentReadMessageId = 0;
        _lastSentReadTime = DateTime.MinValue;
        _lastSentTypingTime = DateTime.MinValue;
    }

    #endregion

    #region Hub subscriptions

    private void SubscribeHubEvents()
    {
        if (_hubConnection is null) return;

        _hubSubscriptions.Add(_hubConnection.On<NotificationDto>("ReceiveNotification", OnNotificationReceived));
        _hubSubscriptions.Add(_hubConnection.On<int>("UserOnline", userId => OnUserStatusChanged(userId, true)));
        _hubSubscriptions.Add(_hubConnection.On<int>("UserOffline", userId => OnUserStatusChanged(userId, false)));
        _hubSubscriptions.Add(_hubConnection.On<UserDto>("UserProfileUpdated", OnUserProfileUpdated));
        _hubSubscriptions.Add(_hubConnection.On<int, int>("UnreadCountUpdated", OnUnreadCountUpdated));
        _hubSubscriptions.Add(_hubConnection.On<MessageDto>("ReceiveMessageDto", OnNewMessageReceived));
        _hubSubscriptions.Add(_hubConnection.On<MessageDto>("MessageUpdated", OnMessageUpdated));
        _hubSubscriptions.Add(_hubConnection.On<MessageDeletedEvent>("MessageDeleted", OnMessageDeleted));
        _hubSubscriptions.Add(_hubConnection.On<int, int>("UserTyping", OnUserTypingReceived));
        _hubSubscriptions.Add(_hubConnection.On<int, int, int?, DateTime?>("MessageRead", OnMessageReadReceived));
        _hubSubscriptions.Add(_hubConnection.On<int, UserDto>("MemberJoined", OnMemberJoinedReceived));
        _hubSubscriptions.Add(_hubConnection.On<int, int>("MemberLeft", OnMemberLeftReceived));
    }

    private void UnsubscribeHubEvents()
    {
        foreach (var sub in _hubSubscriptions)
        {
            try { sub.Dispose(); }
            catch { /* best-effort */ }
        }

        _hubSubscriptions.Clear();
    }

    #endregion

    #region Reconnection

    private async Task OnHubReconnecting(Exception? error)
    {
        Debug.WriteLine($"[GlobalHub] Reconnecting: {error?.Message}");

        if (error?.Message?.Contains("401") == true ||
            error?.Message?.Contains("Unauthorized") == true)
        {
            Debug.WriteLine("[GlobalHub] Attempting token refresh before reconnect...");
            await _authManager.TryRefreshTokenAsync();
        }
    }

    private async Task OnHubReconnectedInternal(string? _)
    {
        Debug.WriteLine("[GlobalHub] Reconnected");
        await LoadUnreadCountsAsync();
        await ReconcileAfterReconnectAsync();
        Reconnected?.Invoke();
    }

    #endregion

    #region Chat-level RPC

    public async Task<ChatReadInfoDto?> GetReadInfoAsync(int chatId)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return null;

        try
        {
            return await _hubConnection.InvokeAsync<ChatReadInfoDto?>("GetReadInfo", chatId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] GetReadInfo error: {ex.Message}");
            return null;
        }
    }

    public async Task MarkMessageAsReadAsync(int chatId, int messageId)
    {
        if (_hubConnection?.State != HubConnectionState.Connected || messageId <= _lastSentReadMessageId)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastSentReadTime).TotalMilliseconds < AppConstants.MarkAsReadDebounceMs)
            return;

        _lastSentReadMessageId = messageId;
        _lastSentReadTime = now;

        try
        {
            await _hubConnection.InvokeAsync("MarkMessageAsRead", chatId, messageId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] MarkMessageAsRead error: {ex.Message}");
        }
    }

    public async Task SendTypingAsync(int chatId)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastSentTypingTime).TotalMilliseconds < AppConstants.TypingSendDebounceMs)
            return;

        _lastSentTypingTime = now;

        try
        {
            await _hubConnection.InvokeAsync("SendTyping", chatId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] SendTyping error: {ex.Message}");
        }
    }

    public async Task<AllUnreadCountsDto?> GetUnreadCountsAsync()
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return null;

        try
        {
            return await _hubConnection.InvokeAsync<AllUnreadCountsDto>("GetUnreadCounts");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] GetUnreadCounts error: {ex.Message}");
            return null;
        }
    }

    public async Task MarkChatAsReadAsync(int chatId)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return;

        try
        {
            SetUnreadCountLocally(chatId, 0);
            await _hubConnection.InvokeAsync("MarkAsRead", chatId, (int?)null);

            try
            {
                await _cacheService.UpdateReadPointerAsync(chatId, null, 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] Cache read pointer update error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] MarkChatAsRead error: {ex.Message}");
        }
    }

    #endregion

    #region Event handlers

    private void OnNotificationReceived(NotificationDto notification)
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        if (!_settingsService.NotificationsEnabled) return;

        if (_openChatId == notification.ChatId)
            return;

        IncrementUnreadCount(notification.ChatId);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _notificationService.Show(notification.ChatName ?? "Новое сообщение",
                    FormatNotificationMessage(notification), DesktopNotificationType.Information, 5000,
                    () => OpenNotificationAsync(notification));

                NotificationReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] Notification display error: {ex.Message}");
            }
        });
    }

    private void OnNewMessageReceived(MessageDto message)
    {
        _ = CacheIncomingMessageAsync(message);

        Dispatcher.UIThread.Post(() => MessageReceivedGlobally?.Invoke(message));

        if (_openChatId == message.ChatId)
            return;

        if (message.SenderId == _authManager.Session.UserId)
            return;

        IncrementUnreadCount(message.ChatId);
    }

    private void OnMessageUpdated(MessageDto message)
    {
        _ = SafeCacheAsync(() => _cacheService.UpsertMessageAsync(message), "update");

        Dispatcher.UIThread.Post(() => MessageUpdatedGlobally?.Invoke(message));
    }

    private void OnMessageDeleted(MessageDeletedEvent evt)
    {
        _ = SafeCacheAsync(() => _cacheService.MarkMessageDeletedAsync(evt.MessageId), "delete");

        Dispatcher.UIThread.Post(() => MessageDeletedGlobally?.Invoke(evt.MessageId, evt.ChatId));
    }

    private void OnUserStatusChanged(int userId, bool isOnline)
        => Dispatcher.UIThread.Post(() => UserStatusChanged?.Invoke(userId, isOnline));

    private void OnUserProfileUpdated(UserDto user)
        => Dispatcher.UIThread.Post(() => UserProfileUpdated?.Invoke(user));

    private void OnUnreadCountUpdated(int chatId, int unreadCount)
    {
        int total;
        lock (_unreadLock)
        {
            var oldCount = _unreadCounts.GetValueOrDefault(chatId, 0);
            _unreadCounts[chatId] = unreadCount;
            _totalUnread = Math.Max(0, _totalUnread - (oldCount - unreadCount));
            total = _totalUnread;
        }

        Dispatcher.UIThread.Post(() =>
        {
            UnreadCountChanged?.Invoke(chatId, unreadCount);
            TotalUnreadChanged?.Invoke(total);
        });
    }

    private void OnUserTypingReceived(int chatId, int userId)
        => Dispatcher.UIThread.Post(() => UserTyping?.Invoke(chatId, userId));

    private void OnMessageReadReceived(int chatId, int userId, int? lastReadMessageId, DateTime? readAt)
        => Dispatcher.UIThread.Post(() => MessageRead?.Invoke(chatId, userId, lastReadMessageId, readAt));

    private void OnMemberJoinedReceived(int chatId, UserDto user)
        => Dispatcher.UIThread.Post(() => MemberJoined?.Invoke(chatId, user));

    private void OnMemberLeftReceived(int chatId, int userId)
        => Dispatcher.UIThread.Post(() => MemberLeft?.Invoke(chatId, userId));

    #endregion

    #region Unread count helpers

    private async Task LoadUnreadCountsAsync()
    {
        try
        {
            var counts = await GetUnreadCountsAsync();
            if (counts is null) return;

            Dictionary<int, int> snapshot;
            int total;

            lock (_unreadLock)
            {
                _unreadCounts.Clear();
                _totalUnread = counts.TotalUnread;

                foreach (var chat in counts.Chats)
                    _unreadCounts[chat.ChatId] = chat.UnreadCount;

                snapshot = new Dictionary<int, int>(_unreadCounts);
                total = _totalUnread;
            }

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var kvp in snapshot)
                    UnreadCountChanged?.Invoke(kvp.Key, kvp.Value);

                TotalUnreadChanged?.Invoke(total);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] LoadUnreadCounts error: {ex.Message}");
        }
    }

    private void SetUnreadCountLocally(int chatId, int newCount)
    {
        int total;
        lock (_unreadLock)
        {
            var oldCount = _unreadCounts.GetValueOrDefault(chatId, 0);
            _unreadCounts[chatId] = newCount;
            _totalUnread = Math.Max(0, _totalUnread - (oldCount - newCount));
            total = _totalUnread;
        }

        Dispatcher.UIThread.Post(() =>
        {
            UnreadCountChanged?.Invoke(chatId, newCount);
            TotalUnreadChanged?.Invoke(total);
        });
    }

    private void IncrementUnreadCount(int chatId)
    {
        int newCount;
        int total;

        lock (_unreadLock)
        {
            newCount = _unreadCounts.GetValueOrDefault(chatId, 0) + 1;
            _unreadCounts[chatId] = newCount;
            _totalUnread++;
            total = _totalUnread;
        }

        Dispatcher.UIThread.Post(() =>
        {
            UnreadCountChanged?.Invoke(chatId, newCount);
            TotalUnreadChanged?.Invoke(total);
        });
    }

    #endregion

    #region Cache helpers

    private async Task CacheIncomingMessageAsync(MessageDto message)
    {
        try
        {
            await _cacheService.UpsertMessageAsync(message);

            var preview = message.Content?.Length > ContentPreviewMaxLength ? message.Content[..ContentPreviewMaxLength] + PreviewEllipsis
                : message.Content;

            await _cacheService.UpdateChatLastMessageAsync(message.ChatId, preview, message.SenderName, message.CreatedAt);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] Cache incoming message error: {ex.Message}");
        }
    }

    private static async Task SafeCacheAsync(Func<Task> action, string operationName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] Cache {operationName} error: {ex.Message}");
        }
    }

    private async Task ReconcileAfterReconnectAsync()
    {
        try
        {
            var chatId = _openChatId;
            if (chatId != NoChatId)
            {
                var syncState = await _cacheService.GetSyncStateAsync(chatId);
                if (syncState?.NewestLoadedId is not null)
                {
                    Debug.WriteLine($"[GlobalHub] Reconcile: gap check for chat {chatId}, newestCached={syncState.NewestLoadedId}");
                }
            }

            Debug.WriteLine("[GlobalHub] Reconciliation check completed");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] Reconciliation error: {ex.Message}");
        }
    }

    #endregion

    #region Notification helpers

    private static string FormatNotificationMessage(NotificationDto notification) =>
        notification.Type == NotificationTypePoll ? notification.Preview ?? "Новый опрос" : $"{notification.SenderName}: {notification.Preview}";

    private Task OpenNotificationAsync(NotificationDto notification)
    {
        if (_navigationService.CurrentViewModel is not MainMenuViewModel mainMenuVm)
            return Task.CompletedTask;

        return mainMenuVm.OpenNotificationAsync(notification);
    }

    #endregion

    #region IDisposable & IAsyncDisposable

    private void DetachFromHub()
    {
        UnsubscribeHubEvents();

        if (_hubConnection is not null)
        {
            _hubConnection.Reconnecting -= OnHubReconnecting;
            _hubConnection.Reconnected -= OnHubReconnectedInternal;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        DetachFromHub();

        var hub = _hubConnection;
        _hubConnection = null;

        if (hub is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await hub.StopAsync();
                    await hub.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GlobalHub] Hub disposal error: {ex.Message}");
                }
            });
        }

        Debug.WriteLine("[GlobalHub] Disposed (sync)");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        DetachFromHub();

        var hub = _hubConnection;
        _hubConnection = null;

        if (hub is not null)
        {
            try
            {
                await hub.StopAsync();
                await hub.DisposeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] DisposeAsync error: {ex.Message}");
            }
        }

        Debug.WriteLine("[GlobalHub] Disposed (async)");
    }

    #endregion
}

public record MessageDeletedEvent(int MessageId, int ChatId);