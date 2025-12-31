using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Storage;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO;
using MessengerShared.DTO.Message;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services;

public interface IGlobalHubConnection : IAsyncDisposable, IDisposable
{
    event Action<NotificationDTO>? NotificationReceived;
    event Action<int, bool>? UserStatusChanged;
    event Action<int, int>? UnreadCountChanged;
    event Action<int>? TotalUnreadChanged;

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    void SetCurrentChat(int? chatId);
    Task<AllUnreadCountsDTO?> GetUnreadCountsAsync();
    Task MarkChatAsReadAsync(int chatId);
    int GetUnreadCount(int chatId);
    int GetTotalUnread();
}

public sealed class GlobalHubConnection(IAuthManager authManager,INotificationService notificationService,ISettingsService settingsService)
    : IGlobalHubConnection
{
    private readonly IAuthManager _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
    private readonly INotificationService _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

    private HubConnection? _hubConnection;
    private bool _disposed;
    private int? _currentChatId;

    private readonly Dictionary<int, int> _unreadCounts = [];
    private int _totalUnread;
    private readonly object _lock = new();

    public event Action<NotificationDTO>? NotificationReceived;
    public event Action<int, bool>? UserStatusChanged;
    public event Action<int, int>? UnreadCountChanged;
    public event Action<int>? TotalUnreadChanged;

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
            _hubConnection.On<NotificationDTO>("ReceiveNotification", OnNotificationReceived);

            // Статус пользователей
            _hubConnection.On<int>("UserOnline", userId => OnUserStatusChanged(userId, true));
            _hubConnection.On<int>("UserOffline", userId => OnUserStatusChanged(userId, false));

            // Обновление счётчика непрочитанных
            _hubConnection.On<int, int>("UnreadCountUpdated", OnUnreadCountUpdated);

            // Новое сообщение в чате
            _hubConnection.On<MessageDTO>("ReceiveMessageDTO", OnNewMessageReceived);

            // Кто-то печатает
            _hubConnection.On<int, int>("UserTyping", (chatId, userId) => Debug.WriteLine($"[GlobalHub] User {userId} is typing in chat {chatId}"));

            _hubConnection.Reconnecting += error =>
            {
                Debug.WriteLine($"[GlobalHub] Reconnecting: {error?.Message}");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async _ =>
            {
                Debug.WriteLine("[GlobalHub] Reconnected");
                await LoadUnreadCountsAsync();
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

    public async Task<AllUnreadCountsDTO?> GetUnreadCountsAsync()
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return null;

        try
        {
            return await _hubConnection.InvokeAsync<AllUnreadCountsDTO>("GetUnreadCounts");
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

    private void OnNotificationReceived(NotificationDTO notification)
    {
        if (!_settingsService.NotificationsEnabled)
        {
            Debug.WriteLine("[GlobalHub] Notifications disabled globally");
            return;
        }

        if (!_currentChatId.HasValue || _currentChatId.Value != notification.ChatId)
        {
            IncrementUnreadCount(notification.ChatId);
        }

        if (_currentChatId == notification.ChatId)
        {
            Debug.WriteLine("[GlobalHub] Skipping notification for current chat");
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
                Debug.WriteLine($"[GlobalHub] Error showing notification: {ex.Message}");
            }
        });
    }

    private void OnNewMessageReceived(MessageDTO message)
    {
        if (_currentChatId == message.ChatId)
            return;

        if (message.SenderId == _authManager.Session.UserId)
            return;

        IncrementUnreadCount(message.ChatId);
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

    private static string FormatNotificationMessage(NotificationDTO notification)
        => notification.Type == "poll"
            ? notification.Preview ?? "Новый опрос"
            : $"{notification.SenderName}: {notification.Preview}";

    private void OnUserStatusChanged(int userId, bool isOnline)
        => Dispatcher.UIThread.Post(() => UserStatusChanged?.Invoke(userId, isOnline));

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
            Debug.WriteLine($"[GlobalHub] Disconnect error: {ex.Message}");
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