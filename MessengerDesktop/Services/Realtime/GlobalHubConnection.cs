using Avalonia.Controls.Notifications;
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
    event Action<MessageDto>? MessageUpdatedGlobally;
    event Action<int, int>? MessageDeletedGlobally;

    /// <summary>Профиль пользователя обновился (аватар, имя, отдел и т.д.).</summary>
    event Action<UserDto>? UserProfileUpdated;

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    void SetCurrentChat(int? chatId);
    Task<AllUnreadCountsDto?> GetUnreadCountsAsync();
    Task MarkChatAsReadAsync(int chatId);
    int GetUnreadCount(int chatId);
    int GetTotalUnread();
}

public sealed class GlobalHubConnection(
    IAuthManager authManager,
    INotificationService notificationService,
    ISettingsService settingsService,
    ILocalCacheService cacheService) : IGlobalHubConnection
{
    private readonly IAuthManager _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
    private readonly INotificationService _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly ILocalCacheService _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

    private HubConnection? _hubConnection;
    private bool _disposed;
    private int? _currentChatId;

    private readonly Dictionary<int, int> _unreadCounts = [];
    private int _totalUnread;
    private readonly object _lock = new();

    public event Action<NotificationDto>? NotificationReceived;
    public event Action<int, bool>? UserStatusChanged;
    public event Action<int, int>? UnreadCountChanged;
    public event Action<int>? TotalUnreadChanged;
    public event Action<MessageDto>? MessageUpdatedGlobally;
    public event Action<int, int>? MessageDeletedGlobally;
    public event Action<UserDto>? UserProfileUpdated;

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
                .WithAutomaticReconnect()
                .Build();

            // Уведомления
            _hubConnection.On<NotificationDto>("ReceiveNotification", OnNotificationReceived);

            // Статус пользователей
            _hubConnection.On<int>("UserOnline", userId => OnUserStatusChanged(userId, true));
            _hubConnection.On<int>("UserOffline", userId => OnUserStatusChanged(userId, false));

            // Обновление профиля пользователя
            _hubConnection.On<UserDto>("UserProfileUpdated", OnUserProfileUpdated);

            // Обновление счётчика непрочитанных
            _hubConnection.On<int, int>("UnreadCountUpdated", OnUnreadCountUpdated);

            // Новое сообщение в чате
            _hubConnection.On<MessageDto>("ReceiveMessageDto", OnNewMessageReceived);

            _hubConnection.On<MessageDto>("MessageUpdated", OnMessageUpdated);
            _hubConnection.On<MessageDeletedEvent>("MessageDeleted", OnMessageDeleted);

            // Кто-то печатает
            _hubConnection.On<int, int>("UserTyping", (chatId, userId) =>
                Debug.WriteLine($"[GlobalHub] User {userId} is typing in chat {chatId}"));

            _hubConnection.Reconnecting += error =>
            {
                Debug.WriteLine($"[GlobalHub] Reconnecting: {error?.Message}");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async _ =>
            {
                Debug.WriteLine("[GlobalHub] Reconnected");
                await LoadUnreadCountsAsync();
                await ReconcileAfterReconnectAsync();
            };

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
                Dictionary<int, int> snapshot;
                int total;
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

            Debug.WriteLine($"[GlobalHub] Loaded unread counts: {_totalUnread} total");
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

            Debug.WriteLine($"[GlobalHub] MarkAsRead sent for chat {chatId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHub] MarkChatAsRead error: {ex.Message}");
        }
    }

    private void SetUnreadCountLocally(int chatId, int newCount)
    {
        int diff;
        lock (_lock)
        {
            var oldCount = _unreadCounts.GetValueOrDefault(chatId, 0);
            diff = oldCount - newCount;
            _unreadCounts[chatId] = newCount;
            _totalUnread = Math.Max(0, _totalUnread - diff);
        }

        Dispatcher.UIThread.Post(() =>
        {
            int count, total;
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
        {
            Debug.WriteLine("[GlobalHub] Уведомления глобально выключены");
            return;
        }

        if (!_currentChatId.HasValue || _currentChatId.Value != notification.ChatId)
        {
            IncrementUnreadCount(notification.ChatId);
        }

        if (_currentChatId == notification.ChatId)
        {
            Debug.WriteLine("[GlobalHub] Пропуск уведомления для текущего чата");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var title = notification.ChatName ?? "Новое сообщение";
                var message = FormatNotificationMessage(notification);
                _notificationService.ShowWindow(title, message, NotificationType.Information, 5000);
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

        if (_currentChatId == message.ChatId)
            return;

        if (message.SenderId == _authManager.Session.UserId)
            return;

        IncrementUnreadCount(message.ChatId);
    }

    private void OnMessageUpdated(MessageDto message)
    {
        Debug.WriteLine($"[GlobalHub] MessageUpdated: id={message.Id}, chat={message.ChatId}");

        _ = Task.Run(async () =>
        {
            try
            {
                await _cacheService.UpsertMessageAsync(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] Cache update error: {ex.Message}");
            }
        });

        Dispatcher.UIThread.Post(() => MessageUpdatedGlobally?.Invoke(message));
    }

    private void OnMessageDeleted(MessageDeletedEvent evt)
    {
        Debug.WriteLine($"[GlobalHub] MessageDeleted: id={evt.MessageId}, chat={evt.ChatId}");

        _ = Task.Run(async () =>
        {
            try
            {
                await _cacheService.MarkMessageDeletedAsync(evt.MessageId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] Cache delete error: {ex.Message}");
            }
        });

        Dispatcher.UIThread.Post(() => MessageDeletedGlobally?.Invoke(evt.MessageId, evt.ChatId));
    }

    private void OnUserStatusChanged(int userId, bool isOnline)
    {
        Debug.WriteLine($"[GlobalHub] UserStatusChanged: userId={userId}, isOnline={isOnline}");
        Dispatcher.UIThread.Post(() => UserStatusChanged?.Invoke(userId, isOnline));
    }

    private void OnUserProfileUpdated(UserDto user)
    {
        Debug.WriteLine($"[GlobalHub] UserProfileUpdated: userId={user.Id}, name={user.DisplayName}");
        Dispatcher.UIThread.Post(() => UserProfileUpdated?.Invoke(user));
    }

    private void OnUnreadCountUpdated(int chatId, int unreadCount)
    {
        int total;
        lock (_lock)
        {
            var oldCount = _unreadCounts.GetValueOrDefault(chatId, 0);
            var diff = oldCount - unreadCount;
            _unreadCounts[chatId] = unreadCount;
            _totalUnread = Math.Max(0, _totalUnread - diff);
            total = _totalUnread;
        }

        Debug.WriteLine($"[GlobalHub] Server unread update: chat={chatId}, count={unreadCount}, total={total}");

        Dispatcher.UIThread.Post(() =>
        {
            UnreadCountChanged?.Invoke(chatId, unreadCount);
            TotalUnreadChanged?.Invoke(total);
        });
    }

    #endregion

    #region Helpers

    private async Task CacheIncomingMessageAsync(MessageDto message)
    {
        try
        {
            await _cacheService.UpsertMessageAsync(message);

            var preview = message.Content?.Length > 100
                ? message.Content[..100] + "..."
                : message.Content;
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
                var syncState = await _cacheService.GetSyncStateAsync(_currentChatId.Value);
                if (syncState?.NewestLoadedId != null)
                {
                    Debug.WriteLine(
                        $"[GlobalHub] Reconcile: checking gap for chat {_currentChatId.Value}, " +
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
        int newCount;
        int total;

        lock (_lock)
        {
            var current = _unreadCounts.GetValueOrDefault(chatId, 0);
            newCount = current + 1;
            _unreadCounts[chatId] = newCount;
            _totalUnread++;
            total = _totalUnread;
        }

        Debug.WriteLine($"[GlobalHub] Incremented unread: chat={chatId}, count={newCount}, total={total}");

        Dispatcher.UIThread.Post(() =>
        {
            UnreadCountChanged?.Invoke(chatId, newCount);
            TotalUnreadChanged?.Invoke(total);
        });
    }

    private static string FormatNotificationMessage(NotificationDto notification)
        => notification.Type == "poll" ? notification.Preview ?? "Новый опрос" : $"{notification.SenderName}: {notification.Preview}";

    #endregion

    public void SetCurrentChat(int? chatId)
    {
        var previousChatId = _currentChatId;
        _currentChatId = chatId;
        Debug.WriteLine($"[GlobalHub] Current chat: {previousChatId?.ToString() ?? "null"} -> {chatId?.ToString() ?? "null"}");
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

        if (_hubConnection != null)
        {
            try
            {
                _hubConnection.StopAsync().GetAwaiter().GetResult();
                _hubConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHub] Dispose error: {ex.Message}");
            }
            _hubConnection = null;
        }

        Debug.WriteLine("[GlobalHub] Disposed (sync)");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hubConnection != null)
        {
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

/// <summary>Dto для десериализации события MessageDeleted с сервера.</summary>
public record MessageDeletedEvent(int MessageId, int ChatId);