using Desktop.Data.Repositories.Abstractions;
using Desktop.Infrastructure.Helpers;
using Desktop.Services.UI;
using Desktop.ViewModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shared.Dto.Online;
using Shared.Hubs;
using System.Diagnostics;

namespace Desktop.Services.Core.Realtime;

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
    public event Action<UserStatusDto>? UserStatusChanged;
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
    public event Action<ChatUpdateEventDto>? ChatUpdated;
    public event Action<int>? ChatRemoved;
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
            UnsubscribeHubEvents();
            _hub = new HubConnectionBuilder().WithUrl($"{App.ApiUrl}chatHub", o => o.AccessTokenProvider = ()
                => Task.FromResult(_auth.Session.Token)).WithAutomaticReconnect().Build();

            SubscribeHubEvents();
            _hub.Reconnecting += OnReconnecting;
            _hub.Reconnected += OnReconnected;

            const int maxRetries = 10;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await _hub.StartAsync(ct);
                    Log("Connected");
                    await LoadUnreadCountsAsync();
                    return;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    Log($"Server not ready (503), retry {i + 1}/{maxRetries}...");
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(2000, ct);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Connect failed after retries: {ex.Message}");
            Volatile.Write(ref _connectState, 0);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_hub is null) return;

        UnsubscribeHubEvents();
        DetachFromHub();

        try { await _hub.StopAsync(); }
        catch (Exception ex) { Log($"Disconnect error: {ex.Message}"); }
        finally
        {
            lock (_unreadLock)
            {
                _unreadCounts.Clear();
                _totalUnread = 0;
            }
            _openChatId = NoChatId;
            _lastSentReadMsgId = 0;
            _lastReadTime = DateTime.MinValue;
            _lastTypingTime = DateTime.MinValue;
            _connectState = 0;
            _hub = null;
        }
    }
    public void ResetForNewSession()
    {
        UnsubscribeHubEvents();

        lock (_unreadLock)
        {
            _unreadCounts.Clear();
            _totalUnread = 0;
        }
        _openChatId = NoChatId;
        _lastSentReadMsgId = 0;
        _lastReadTime = DateTime.MinValue;
        _lastTypingTime = DateTime.MinValue;
        Volatile.Write(ref _connectState, 0);
    }

    public void SetCurrentChat(int? chatId)
    {
        _openChatId = chatId ?? NoChatId;
        _lastSentReadMsgId = 0;
        _lastReadTime = _lastTypingTime = DateTime.MinValue;
    }

    #endregion

    #region Hub invoke helpers

    private async Task<T?> SafeInvokeAsync<T>(string method, params object?[] args)
    {
        if (_hub?.State != HubConnectionState.Connected) return default;
        try
        {
            return args.Length switch
            {
                0 => await _hub.InvokeAsync<T>(method),
                1 => await _hub.InvokeAsync<T>(method, args[0]),
                2 => await _hub.InvokeAsync<T>(method, args[0], args[1]),
                _ => throw new InvalidOperationException($"Unexpected args count: {args.Length}")
            };
        }
        catch (Exception ex) { Log($"{method} error: {ex.Message}"); return default; }
    }

    private async Task SafeInvokeAsync(string method, params object?[] args)
    {
        if (_hub?.State != HubConnectionState.Connected) return;
        try
        {
            switch (args.Length)
            {
                case 0: await _hub.InvokeAsync(method); break;
                case 1: await _hub.InvokeAsync(method, args[0]); break;
                case 2: await _hub.InvokeAsync(method, args[0], args[1]); break;
                default: throw new InvalidOperationException($"Unexpected args count: {args.Length}");
            }
        }
        catch (Exception ex) { Log($"{method} error: {ex.Message}"); }
    }

    #endregion

    #region Chat-level RPC

    public Task<ChatReadInfoDto?> GetReadInfoAsync(int chatId)
        => SafeInvokeAsync<ChatReadInfoDto?>(HubMethods.ChatInvoke.GetReadInfo, chatId);

    public Task<AllUnreadCountsDto?> GetUnreadCountsAsync()
        => SafeInvokeAsync<AllUnreadCountsDto?>(HubMethods.ChatInvoke.GetUnreadCounts);

    public async Task MarkMessageAsReadAsync(int chatId, int messageId)
    {
        if (_hub?.State != HubConnectionState.Connected || messageId <= _lastSentReadMsgId) return;
        var now = DateTime.UtcNow;
        if ((now - _lastReadTime).TotalMilliseconds < AppConstants.MarkAsReadDebounceMs) return;
        _lastSentReadMsgId = messageId;
        _lastReadTime = now;
        await SafeInvokeAsync(HubMethods.ChatInvoke.MarkMessageAsRead, chatId, messageId);
    }

    public async Task SendTypingAsync(int chatId)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTypingTime).TotalMilliseconds < AppConstants.TypingSendDebounceMs) return;
        _lastTypingTime = now;
        await SafeInvokeAsync(HubMethods.ChatInvoke.SendTyping, chatId);
    }

    public async Task MarkChatAsReadAsync(int chatId)
    {
        if (_hub?.State != HubConnectionState.Connected) return;
        try
        {
            UpdateUnread(chatId, 0);
            await _hub.InvokeAsync(HubMethods.ChatInvoke.MarkAsRead, chatId, null);
            await SafeCacheAsync(() => _cache.UpdateReadPointerAsync(chatId, null, 0), "read pointer");
        }
        catch (Exception ex) { Log($"MarkChatAsRead error: {ex.Message}"); }
    }

    public async Task SetStatusAsync(UserStatusType status, string? duration = null)
    {
        if (_hub?.State != HubConnectionState.Connected)
        {
            Log($"[SetStatus] Hub not connected, state={_hub?.State}");
            return;
        }

        Log($"[SetStatus] Invoking: status={(int)status} ({status}), duration={duration ?? "null"}");

        try
        {
            await _hub.InvokeAsync(HubMethods.ChatInvoke.SetStatus, (int)status, duration);
            Log("[SetStatus] Success");
        }
        catch (HubException hex)
        {
            Log($"[SetStatus] HubException: {hex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Log($"[SetStatus] Error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    #endregion

    #region Hub subscriptions

    private void SubscribeHubEvents()
    {
        if (_hub is null) return;

        _subs.Add(_hub.On<NotificationDto>(HubMethods.Chat.ReceiveNotification, OnNotificationReceived));
        _subs.Add(_hub.On<int>(HubMethods.Chat.UserOnline, id => Log($"UserOnline: {id}")));
        _subs.Add(_hub.On<int>(HubMethods.Chat.UserOffline, id =>
        {
            Log($"UserOffline: {id}");
            PostUI(() => UserStatusChanged?.Invoke(new UserStatusDto(id, false, DateTime.UtcNow, UserStatusType.Online, null)));
        }));
        _subs.Add(_hub.On<UserStatusDto>(HubMethods.Chat.UserStatusChanged, dto =>
        {
            Log($"UserStatusChanged: userId={dto.UserId}");
            PostUI(() => UserStatusChanged?.Invoke(dto));
        }));
        _subs.Add(_hub.On<UserDto>(HubMethods.Chat.UserProfileUpdated, u => PostUI(() => UserProfileUpdated?.Invoke(u))));
        _subs.Add(_hub.On<int, int>(HubMethods.Chat.UnreadCountUpdated, (cid, cnt) => UpdateUnread(cid, cnt)));
        _subs.Add(_hub.On<MessageDto>(HubMethods.Chat.ReceiveMessage, OnNewMessageReceived));
        _subs.Add(_hub.On<MessageDto>(HubMethods.Chat.MessageUpdated, msg =>
        {
            Debug.WriteLine($"[GlobalHub] ← MessageUpdated received: id={msg.Id} chatId={msg.ChatId} IsPinned={msg.IsPinned}");
            OnMessageUpdated(msg);
        }));
        _subs.Add(_hub.On<PollDto>(HubMethods.Chat.PollUpdated, OnPollUpdated));
        _subs.Add(_hub.On<MessageDeletedEvent>(HubMethods.Chat.MessageDeleted, OnMessageDeleted));
        _subs.Add(_hub.On<int, int>(HubMethods.Chat.UserTyping, (c, u) => PostUI(() => UserTyping?.Invoke(c, u))));
        _subs.Add(_hub.On<int, int, int?, DateTime?>(HubMethods.Chat.MessageRead, (c, u, m, t) => PostUI(() => MessageRead?.Invoke(c, u, m, t))));
        _subs.Add(_hub.On<int, UserDto>(HubMethods.Chat.MemberJoined, (c, u) => PostUI(() => MemberJoined?.Invoke(c, u))));
        _subs.Add(_hub.On<int, int>(HubMethods.Chat.MemberLeft, (c, u) => PostUI(() => MemberLeft?.Invoke(c, u))));
        _subs.Add(_hub.On<ChatUpdateEventDto>(HubMethods.Chat.ChatUpdated, OnChatUpdated));
        _subs.Add(_hub.On<int>(HubMethods.Chat.ChatRemoved, chatId => PostUI(() => ChatRemoved?.Invoke(chatId))));
    }

    private void OnChatUpdated(ChatUpdateEventDto update)
    {
        _ = SafeCacheAsync(() => _cache.PatchChatMetaAsync(update), "chat meta update");
        PostUI(() => ChatUpdated?.Invoke(update));
    }

    private void UnsubscribeHubEvents()
    {
        foreach (var s in _subs) try { s.Dispose(); } catch { /* ignored */ }
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
                    DesktopNotificationType.Information, 5000, () => _nav.CurrentViewModel is MainMenuViewModel vm ? vm.OpenNotificationAsync(n) : Task.CompletedTask);

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
        Debug.WriteLine($"[GlobalHub] OnMessageUpdated processing: id={msg.Id} chatId={msg.ChatId} IsPinned={msg.IsPinned}");
        _ = SafeCacheAsync(() => _cache.UpsertMessageAsync(msg), "update");
        PostUI(() => MessageUpdatedGlobally?.Invoke(msg));
    }

    private void OnMessageDeleted(MessageDeletedEvent evt)
    {
        _ = SafeCacheAsync(() => _cache.MarkMessageDeletedAsync(evt.MessageId), "delete");
        PostUI(() => MessageDeletedGlobally?.Invoke(evt.MessageId, evt.ChatId));
    }

    private void OnPollUpdated(PollDto poll)
    {
        _ = SafeCacheAsync(() => _cache.UpdatePollThreadAsync(poll), "poll update");
        PostUI(() => PollUpdatedGlobally?.Invoke(poll));
    }

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
            var (preview, _) = ChatPreviewFormatter.BuildPreviewWithMeta(msg, uid);
            var senderName = ChatPreviewFormatter.FormatSenderName(msg.SenderName, msg.SenderId, uid);

            var hasFilesOnly = msg.Files is { Count: > 0 } && string.IsNullOrWhiteSpace(msg.Content) && !msg.IsVoiceMessage;

            await _cache.UpdateChatLastMessageAsync(msg.ChatId, preview, senderName, msg.CreatedAt);
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