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
    event Action<VoiceTranscriptionDto>? TranscriptionStatusChanged;
    event Action<VoiceTranscriptionDto>? TranscriptionCompleted;
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

public sealed class GlobalHubConnection(IAuthManager authManager,INotificationService notificationService,
    INavigationService navigationService, ISettingsService settingsService, ILocalCacheService cacheService) : IGlobalHubConnection
{
    private readonly IAuthManager _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
    private readonly INotificationService _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly ILocalCacheService _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

    private HubConnection? _hubConnection;
    private readonly List<IDisposable> _hubSubscriptions = [];
    private bool _disposed;
    private int? _currentChatId;

    private readonly Dictionary<int, int> _unreadCounts = [];
    private int _totalUnread;
    private readonly Lock _lock = new();

    private int _lastSentReadMessageId;
    private DateTime _lastSentReadTime = DateTime.MinValue;
    private DateTime _lastSentTypingTime = DateTime.MinValue;

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
    public event Action<VoiceTranscriptionDto>? TranscriptionStatusChanged;
    public event Action<VoiceTranscriptionDto>? TranscriptionCompleted;
    public event Action? Reconnected;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public int GetUnreadCount(int chatId)
    {
        lock (_lock)
        {
            return _unreadCounts.GetValueOrDefault(chatId, 0);
        }
    }

    public int GetTotalUnread()
    {
        lock (_lock)
        {
            return _totalUnread;
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_hubConnection != null) return;

        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{App.ApiUrl}chatHub", options =>
                options.AccessTokenProvider = () => Task.FromResult(_authManager.Session.Token))
                .WithAutomaticReconnect().Build();

            SubscribeHubEvents();

            _hubConnection.Reconnecting += OnHubReconnecting;
            _hubConnection.Reconnected += OnHubReconnectedInternal;

            await _hubConnection.StartAsync(ct);
            Debug.WriteLine("[GlobalHub] Connected successfully");

            await LoadUnreadCountsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] Connection error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Подписка на все Hub-события. Сохраняет IDisposable для корректной отписки.
    /// </summary>
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
        _hubSubscriptions.Add(_hubConnection.On<VoiceTranscriptionDto>("TranscriptionStatusChanged", OnTranscriptionStatusChangedReceived));
        _hubSubscriptions.Add(_hubConnection.On<VoiceTranscriptionDto>("TranscriptionCompleted", OnTranscriptionCompletedReceived));
    }

    /// <summary>
    /// Отписка от всех Hub-событий через сохранённые IDisposable.
    /// </summary>
    private void UnsubscribeHubEvents()
    {
        foreach (var sub in _hubSubscriptions)
        {
            try { sub.Dispose(); }
            catch { /* best-effort */ }
        }
        _hubSubscriptions.Clear();
    }

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

    #region Chat-level methods

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

    #endregion

    private async Task LoadUnreadCountsAsync()
    {
        try
        {
            var counts = await GetUnreadCountsAsync();
            if (counts == null) return;

            lock (_lock)
            {
                _unreadCounts.Clear();
                _totalUnread = counts.TotalUnread;

                foreach (var chat in counts.Chats)
                {
                    _unreadCounts[chat.ChatId] = chat.UnreadCount;
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                Dictionary<int, int> snapshot = [];
                int total = 0;

                lock (_lock)
                {
                    snapshot = new Dictionary<int, int>(_unreadCounts);
                    total = _totalUnread;
                }

                foreach (var kvp in snapshot)
                {
                    UnreadCountChanged?.Invoke(kvp.Key, kvp.Value);
                }
                TotalUnreadChanged?.Invoke(total);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] LoadUnreadCounts error: {ex.Message}");
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

    private void SetUnreadCountLocally(int chatId, int newCount)
    {
        lock (_lock)
        {
            var oldCount = _unreadCounts.GetValueOrDefault(chatId, 0);
            var diff = oldCount - newCount;
            _unreadCounts[chatId] = newCount;
            _totalUnread = Math.Max(0, _totalUnread - diff);
        }

        Dispatcher.UIThread.Post(() =>
        {
            int count = 0;
            int total = 0;

            lock (_lock)
            {
                count = _unreadCounts.GetValueOrDefault(chatId, 0);
                total = _totalUnread;
            }
            UnreadCountChanged?.Invoke(chatId, count);
            TotalUnreadChanged?.Invoke(total);
        });
    }

    #region Event Handlers

    private void OnNotificationReceived(NotificationDto notification)
    {
        if (!_settingsService.NotificationsEnabled)
            return;

        if (!_currentChatId.HasValue ||
            _currentChatId.Value != notification.ChatId)
        {
            IncrementUnreadCount(notification.ChatId);
        }

        if (_currentChatId == notification.ChatId)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var title = notification.ChatName ?? "Новое сообщение";
                var message = FormatNotificationMessage(notification);
                _notificationService.ShowWindow(title, message, DesktopNotificationType.Information, 5000,
                    () => OpenNotificationAsync(notification));

                NotificationReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] Ошибка отображения уведомления: {ex.Message}");
            }
        });
    }

    private void OnNewMessageReceived(MessageDto message)
    {
        _ = CacheIncomingMessageAsync(message);

        Dispatcher.UIThread.Post(
            () => MessageReceivedGlobally?.Invoke(message));

        if (_currentChatId == message.ChatId)
            return;

        if (message.SenderId == _authManager.Session.UserId)
            return;

        IncrementUnreadCount(message.ChatId);
    }

    private void OnMessageUpdated(MessageDto message)
    {
        _ = Task.Run(async () =>
        {
            try { await _cacheService.UpsertMessageAsync(message); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] Cache update error: {ex.Message}");
            }
        });

        Dispatcher.UIThread.Post(() => MessageUpdatedGlobally?.Invoke(message));
    }

    private void OnMessageDeleted(MessageDeletedEvent evt)
    {
        _ = Task.Run(async () =>
        {
            try { await _cacheService.MarkMessageDeletedAsync(evt.MessageId); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] Cache delete error: {ex.Message}");
            }
        });

        Dispatcher.UIThread.Post(() => MessageDeletedGlobally?.Invoke(evt.MessageId, evt.ChatId));
    }

    private void OnUserStatusChanged(int userId, bool isOnline)
        => Dispatcher.UIThread.Post(() => UserStatusChanged?.Invoke(userId, isOnline));

    private void OnUserProfileUpdated(UserDto user)
        => Dispatcher.UIThread.Post(() => UserProfileUpdated?.Invoke(user));

    private void OnUnreadCountUpdated(int chatId, int unreadCount)
    {
        int total = 0;

        lock (_lock)
        {
            var oldCount = _unreadCounts.GetValueOrDefault(chatId, 0);
            var diff = oldCount - unreadCount;
            _unreadCounts[chatId] = unreadCount;
            _totalUnread = Math.Max(0, _totalUnread - diff);
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

    private void OnTranscriptionStatusChangedReceived(VoiceTranscriptionDto transcription)
       => Dispatcher.UIThread.Post(() => TranscriptionStatusChanged?.Invoke(transcription));

    private void OnTranscriptionCompletedReceived(VoiceTranscriptionDto transcription)
        => Dispatcher.UIThread.Post(() => TranscriptionCompleted?.Invoke(transcription));

    #endregion

    #region Helpers

    private async Task CacheIncomingMessageAsync(MessageDto message)
    {
        try
        {
            await _cacheService.UpsertMessageAsync(message);

            var preview = message.Content?.Length > 100
                ? message.Content[..100] + "..." : message.Content;
            await _cacheService.UpdateChatLastMessageAsync(
                message.ChatId, preview, message.SenderName, message.CreatedAt);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] Cache incoming message error: {ex.Message}");
        }
    }

    private async Task ReconcileAfterReconnectAsync()
    {
        try
        {
            if (_currentChatId.HasValue)
            {
                var syncState = await _cacheService.GetSyncStateAsync(
                    _currentChatId.Value);
                if (syncState?.NewestLoadedId != null)
                {
                    Debug.WriteLine($"[GlobalHub] Reconcile: checking gap for chat {_currentChatId.Value}, " +
                        $"newestCached={syncState.NewestLoadedId}");
                }
            }

            Debug.WriteLine("[GlobalHub] Reconciliation check completed");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] Reconciliation error: {ex.Message}");
        }
    }

    private void IncrementUnreadCount(int chatId)
    {
        int newCount = 0;
        int total = 0;

        lock (_lock)
        {
            var current = _unreadCounts.GetValueOrDefault(chatId, 0);
            newCount = current + 1;
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

    private static string FormatNotificationMessage(NotificationDto notification)
        => notification.Type == "poll" ? notification.Preview ?? "Новый опрос"
            : $"{notification.SenderName}: {notification.Preview}";

    private Task OpenNotificationAsync(NotificationDto notification)
    {
        if (_navigationService.CurrentViewModel is not MainMenuViewModel mainMenuViewModel)
            return Task.CompletedTask;

        return mainMenuViewModel.OpenNotificationAsync(notification);
    }
    #endregion

    public void SetCurrentChat(int? chatId)
    {
        _currentChatId = chatId;

        _lastSentReadMessageId = 0;
        _lastSentReadTime = DateTime.MinValue;
        _lastSentTypingTime = DateTime.MinValue;
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection == null) return;
        try
        {
            await _hubConnection.StopAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] Ошибка отключения: {ex.Message}");
        }
    }

    #region IDisposable & IAsyncDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeHubEvents();

        if (_hubConnection != null)
        {
            _hubConnection.Reconnecting -= OnHubReconnecting;
            _hubConnection.Reconnected -= OnHubReconnectedInternal;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }
                catch { /* не блокируем Dispose на случай ошибок при остановке/уничтожении соединения */ }
            });
            _hubConnection = null;
        }

        Debug.WriteLine("[GlobalHub] Disposed (sync)");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeHubEvents();

        if (_hubConnection != null)
        {
            _hubConnection.Reconnecting -= OnHubReconnecting;
            _hubConnection.Reconnected -= OnHubReconnectedInternal;

            try
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] DisposeAsync error: {ex.Message}");
            }
            _hubConnection = null;
        }

        Debug.WriteLine("[GlobalHub] Disposed (async)");
    }

    #endregion
}

public record MessageDeletedEvent(int MessageId, int ChatId);