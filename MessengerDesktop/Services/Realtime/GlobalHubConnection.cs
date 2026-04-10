using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Helpers;
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
    event Action<PollDto>? PollUpdatedGlobally;
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

public sealed class GlobalHubConnection(IAuthManager authManager, INotificationService notificationService, INavigationService navigationService,
    ISettingsService settingsService, ILocalCacheService cacheService) : IGlobalHubConnection
{
    private const int NoChatId = -1;
    private const string NotificationTypePoll = "poll";

    private readonly IAuthManager _auth = authManager ?? throw new ArgumentNullException(nameof(authManager));
    private readonly INotificationService _notify = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    private readonly INavigationService _nav = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly ISettingsService _settings = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly ILocalCacheService _cache = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

    private HubConnection? _hub;
    private readonly List<IDisposable> _subs = [];
    private readonly Dictionary<int, int> _unreadCounts = [];
    private readonly Lock _unreadLock = new();

    private int _disposed, _connectState, _totalUnread, _lastSentReadMsgId;
    private volatile int _openChatId = NoChatId;
    private DateTime _lastReadTime = DateTime.MinValue, _lastTypingTime = DateTime.MinValue;

    #region Events

    public event Action<NotificationDto>? NotificationReceived;
    public event Action<int, bool>? UserStatusChanged;
    public event Action<int, int>? UnreadCountChanged;
    public event Action<int>? TotalUnreadChanged;
    public event Action<MessageDto>? MessageReceivedGlobally;
    public event Action<MessageDto>? MessageUpdatedGlobally;
    public event Action<PollDto>? PollUpdatedGlobally;
    public event Action<int, int>? MessageDeletedGlobally;
    public event Action<UserDto>? UserProfileUpdated;
    public event Action<int, int>? UserTyping;
    public event Action<int, int, int?, DateTime?>? MessageRead;
    public event Action<int, UserDto>? MemberJoined;
    public event Action<int, int>? MemberLeft;
    public event Action? Reconnected;

    #endregion

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public int GetUnreadCount(int chatId) { lock (_unreadLock) return _unreadCounts.GetValueOrDefault(chatId); }
    public int GetTotalUnread() { lock (_unreadLock) return _totalUnread; }

    private static void PostUI(Action a) => Dispatcher.UIThread.Post(a);
    private static void Log(string msg) => Debug.WriteLine($"[GlobalHub] {msg}");

    #region Connection

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _connectState, 1, 0) != 0) return;
        try
        {
            _hub = new HubConnectionBuilder().WithUrl($"{App.ApiUrl}chatHub",
                o => o.AccessTokenProvider = () => Task.FromResult(_auth.Session.Token)).WithAutomaticReconnect().Build();

            SubscribeHubEvents();
            _hub.Reconnecting += OnReconnecting;
            _hub.Reconnected += OnReconnected;
            await _hub.StartAsync(ct);
            Log("Connected");
            await LoadUnreadCountsAsync();
        }
        catch { Volatile.Write(ref _connectState, 0); throw; }
    }

    public async Task DisconnectAsync()
    {
        if (_hub is null) return;
        try { await _hub.StopAsync(); }
        catch (Exception ex) { Log($"Disconnect error: {ex.Message}"); }
    }

    public void SetCurrentChat(int? chatId)
    {
        _openChatId = chatId ?? NoChatId;
        _lastSentReadMsgId = 0;
        _lastReadTime = _lastTypingTime = DateTime.MinValue;
    }

    #endregion

    #region Hub invoke helpers

    private async Task<T?> InvokeAsync<T>(string method, params object[] args)
    {
        if (_hub?.State != HubConnectionState.Connected) return default;
        try { return await _hub.InvokeAsync<T>(method, args); }
        catch (Exception ex) { Log($"{method} error: {ex.Message}"); return default; }
    }

    private async Task InvokeAsync(string method, params object[] args)
    {
        if (_hub?.State != HubConnectionState.Connected) return;
        try { await _hub.InvokeAsync(method, args); }
        catch (Exception ex) { Log($"{method} error: {ex.Message}"); }
    }

    #endregion

    #region Chat-level RPC

    public Task<ChatReadInfoDto?> GetReadInfoAsync(int chatId)
        => InvokeAsync<ChatReadInfoDto?>("GetReadInfo", chatId);

    public async Task MarkMessageAsReadAsync(int chatId, int messageId)
    {
        if (_hub?.State != HubConnectionState.Connected || messageId <= _lastSentReadMsgId) return;
        var now = DateTime.UtcNow;
        if ((now - _lastReadTime).TotalMilliseconds < AppConstants.MarkAsReadDebounceMs) return;
        _lastSentReadMsgId = messageId;
        _lastReadTime = now;
        await InvokeAsync("MarkMessageAsRead", chatId, messageId);
    }

    public async Task SendTypingAsync(int chatId)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTypingTime).TotalMilliseconds < AppConstants.TypingSendDebounceMs) return;
        _lastTypingTime = now;
        await InvokeAsync("SendTyping", chatId);
    }

    public Task<AllUnreadCountsDto?> GetUnreadCountsAsync()
        => InvokeAsync<AllUnreadCountsDto?>("GetUnreadCounts");

    public async Task MarkChatAsReadAsync(int chatId)
    {
        if (_hub?.State != HubConnectionState.Connected) return;
        try
        {
            UpdateUnread(chatId, 0);
            await _hub.InvokeAsync("MarkAsRead", chatId, (int?)null);
            await SafeCacheAsync(() => _cache.UpdateReadPointerAsync(chatId, null, 0), "read pointer");
        }
        catch (Exception ex) { Log($"MarkChatAsRead error: {ex.Message}"); }
    }

    #endregion

    #region Hub subscriptions

    private void SubscribeHubEvents()
    {
        if (_hub is null) return;
        _subs.Add(_hub.On<NotificationDto>("ReceiveNotification", OnNotificationReceived));
        _subs.Add(_hub.On<int>("UserOnline", id => PostUI(() => UserStatusChanged?.Invoke(id, true))));
        _subs.Add(_hub.On<int>("UserOffline", id => PostUI(() => UserStatusChanged?.Invoke(id, false))));
        _subs.Add(_hub.On<UserDto>("UserProfileUpdated", u => PostUI(() => UserProfileUpdated?.Invoke(u))));
        _subs.Add(_hub.On<int, int>("UnreadCountUpdated", (cid, cnt) => UpdateUnread(cid, cnt)));
        _subs.Add(_hub.On<MessageDto>("ReceiveMessageDto", OnNewMessageReceived));
        _subs.Add(_hub.On<MessageDto>("MessageUpdated", OnMessageUpdated));
        _subs.Add(_hub.On<PollDto>("ReceivePollUpdate", OnPollUpdated));
        _subs.Add(_hub.On<MessageDeletedEvent>("MessageDeleted", OnMessageDeleted));
        _subs.Add(_hub.On<int, int>("UserTyping", (c, u) => PostUI(() => UserTyping?.Invoke(c, u))));
        _subs.Add(_hub.On<int, int, int?, DateTime?>("MessageRead", (c, u, m, t) => PostUI(() => MessageRead?.Invoke(c, u, m, t))));
        _subs.Add(_hub.On<int, UserDto>("MemberJoined", (c, u) => PostUI(() => MemberJoined?.Invoke(c, u))));
        _subs.Add(_hub.On<int, int>("MemberLeft", (c, u) => PostUI(() => MemberLeft?.Invoke(c, u))));
    }

    private void UnsubscribeHubEvents()
    {
        foreach (var s in _subs) try { s.Dispose(); } catch { /* Ignored */ }
        _subs.Clear();
    }

    #endregion

    #region Reconnection

    private async Task OnReconnecting(Exception? err)
    {
        Log($"Reconnecting: {err?.Message}");
        if (err?.Message?.Contains("401") == true || err?.Message?.Contains("Unauthorized") == true)
        {
            Log("Refreshing token...");
            await _auth.TryRefreshTokenAsync();
        }
    }

    private async Task OnReconnected(string? _)
    {
        Log("Reconnected");
        await LoadUnreadCountsAsync();
        await ReconcileAfterReconnectAsync();
        Reconnected?.Invoke();
    }

    #endregion

    #region Event handlers

    private void OnNotificationReceived(NotificationDto n)
    {
        if (Volatile.Read(ref _disposed) == 1 || !_settings.NotificationsEnabled || _openChatId == n.ChatId) return;

        PostUI(() =>
        {
            try
            {
                _notify.Show(n.ChatName ?? "Новое сообщение", n.Type == NotificationTypePoll ? n.Preview ?? "Новый опрос" : $"{n.SenderName}: {n.Preview}",
                    DesktopNotificationType.Information, 5000, () => _nav.CurrentViewModel is MainMenuViewModel vm
                        ? vm.OpenNotificationAsync(n) : Task.CompletedTask);
                NotificationReceived?.Invoke(n);
            }
            catch (Exception ex) { Log($"Ошибка отображения уведомления: {ex.Message}"); }
        });
    }

    private void OnNewMessageReceived(MessageDto msg)
    {
        _ = CacheIncomingMessageAsync(msg);
        PostUI(() => MessageReceivedGlobally?.Invoke(msg));
        if (_openChatId != msg.ChatId && msg.SenderId != _auth.Session.UserId)
            IncrementUnread(msg.ChatId);
    }

    private void OnMessageUpdated(MessageDto msg)
    {
        _ = SafeCacheAsync(() => _cache.UpsertMessageAsync(msg), "update");
        PostUI(() => MessageUpdatedGlobally?.Invoke(msg));
    }

    private void OnMessageDeleted(MessageDeletedEvent evt)
    {
        _ = SafeCacheAsync(() => _cache.MarkMessageDeletedAsync(evt.MessageId), "delete");
        PostUI(() => MessageDeletedGlobally?.Invoke(evt.MessageId, evt.ChatId));
    }
    private void OnPollUpdated(PollDto poll) => PostUI(() => PollUpdatedGlobally?.Invoke(poll));

    #endregion

    #region Unread helpers

    private void UpdateUnread(int chatId, int newCount)
    {
        int total;
        lock (_unreadLock)
        {
            var old = _unreadCounts.GetValueOrDefault(chatId);
            _unreadCounts[chatId] = newCount;
            _totalUnread = Math.Max(0, _totalUnread - old + newCount);
            total = _totalUnread;
        }
        PostUI(() => { UnreadCountChanged?.Invoke(chatId, newCount); TotalUnreadChanged?.Invoke(total); });
    }

    private void IncrementUnread(int chatId)
    {
        int val, total;
        lock (_unreadLock)
        {
            val = _unreadCounts.GetValueOrDefault(chatId) + 1;
            _unreadCounts[chatId] = val;
            total = ++_totalUnread;
        }
        PostUI(() => { UnreadCountChanged?.Invoke(chatId, val); TotalUnreadChanged?.Invoke(total); });
    }

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
                foreach (var c in counts.Chats) _unreadCounts[c.ChatId] = c.UnreadCount;
                snapshot = new(_unreadCounts);
                total = _totalUnread;
            }
            PostUI(() =>
            {
                foreach (var kv in snapshot) UnreadCountChanged?.Invoke(kv.Key, kv.Value);
                TotalUnreadChanged?.Invoke(total);
            });
        }
        catch (Exception ex) { Log($"LoadUnreadCounts error: {ex.Message}"); }
    }

    #endregion

    #region Cache helpers

    private async Task CacheIncomingMessageAsync(MessageDto msg)
    {
        try
        {
            await _cache.UpsertMessageAsync(msg);
            var uid = _auth.Session.UserId;
            await _cache.UpdateChatLastMessageAsync(msg.ChatId, ChatPreviewFormatter.BuildPreview(msg),
                ChatPreviewFormatter.FormatSenderName(msg.SenderName, msg.SenderId, uid), msg.CreatedAt);
        }
        catch (Exception ex) { Log($"Cache incoming error: {ex.Message}"); }
    }

    private static async Task SafeCacheAsync(Func<Task> action, string op)
    {
        try { await action(); }
        catch (Exception ex) { Log($"Cache {op} error: {ex.Message}"); }
    }

    private async Task ReconcileAfterReconnectAsync()
    {
        try
        {
            var chatId = _openChatId;
            if (chatId != NoChatId)
            {
                var sync = await _cache.GetSyncStateAsync(chatId);
                if (sync?.NewestLoadedId is not null)
                    Log($"Reconcile: gap check chat {chatId}, newest={sync.NewestLoadedId}");
            }
            Log("Reconciliation completed");
        }
        catch (Exception ex) { Log($"Reconciliation error: {ex.Message}"); }
    }

    #endregion

    #region Dispose

    private void DetachFromHub()
    {
        UnsubscribeHubEvents();
        if (_hub is not null)
        {
            _hub.Reconnecting -= OnReconnecting;
            _hub.Reconnected -= OnReconnected;
        }
    }

    private async Task DisposeHubAsync()
    {
        var hub = _hub;
        _hub = null;
        if (hub is null) return;
        try { await hub.StopAsync(); await hub.DisposeAsync(); }
        catch (Exception ex) { Log($"Hub disposal error: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        DetachFromHub();
        _ = Task.Run(DisposeHubAsync);
        Log("Disposed (sync)");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        DetachFromHub();
        await DisposeHubAsync();
        Log("Disposed (async)");
    }

    #endregion
}

public record MessageDeletedEvent(int MessageId, int ChatId);