using MessengerDesktop.Data.Entities;
using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Infrastructure;
using MessengerDesktop.Services.Audio;
using MessengerDesktop.Services.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public sealed class ChatMessageManager(
    int chatId, int userId, IApiClientService apiClient,
    Func<ObservableCollection<UserDto>> getMembersFunc,
    IFileDownloadService? downloadService = null,
    INotificationService? notificationService = null,
    ILocalCacheService? cacheService = null,
    IAudioPlayerService? audioPlayer = null) : IAsyncDisposable
{
    private readonly IAudioPlayerService? _audioPlayer = audioPlayer;
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly Func<ObservableCollection<UserDto>> _getMembersFunc = getMembersFunc ?? throw new ArgumentNullException(nameof(getMembersFunc));

    private int? _oldestLoadedMessageId;
    private int? _newestLoadedMessageId;
    private volatile bool _hasMoreOlder = true;
    private volatile bool _hasMoreNewer;
    private readonly HashSet<int> _loadedMessageIds = [];

    private int _isLoading;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Lock _bgLock = new();
    private readonly List<Task> _backgroundTasks = [];

    private const int MaxGapFillBatches = 5;
    private const int MaxMessagesInMemory = 200;
    private const int TrimBatchSize = 80;

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public bool IsLoading => Volatile.Read(ref _isLoading) != 0;
    public bool HasMoreOlder => _hasMoreOlder;
    public bool HasMoreNewer => _hasMoreNewer;
    public int? LastReadMessageId { get; private set; }
    public int? FirstUnreadMessageId { get; private set; }

    private bool TryBeginLoading()
        => Interlocked.CompareExchange(ref _isLoading, 1, 0) == 0;

    private void EndLoading()
        => Interlocked.Exchange(ref _isLoading, 0);

    public void SetReadInfo(ChatReadInfoDto? info)
    {
        if (info == null) return;
        LastReadMessageId = info.LastReadMessageId;
        FirstUnreadMessageId = info.FirstUnreadMessageId;
        Debug.WriteLine($"[MessageManager] ReadInfo: lastRead={LastReadMessageId}, firstUnread={FirstUnreadMessageId}");
    }

    #region Message Loading

    public async Task<int?> LoadInitialMessagesAsync(CancellationToken ct = default)
    {
        if (!TryBeginLoading()) return null;

        try
        {
            if (FirstUnreadMessageId.HasValue)
                return await LoadAroundCoreAsync(FirstUnreadMessageId.Value, ct);

            if (await TryLoadInitialFromCacheAsync() is { } cachedIndex)
                return cachedIndex;

            return await LoadInitialFromServerAsync(ct);
        }
        finally { EndLoading(); }
    }

    private async Task<int?> TryLoadInitialFromCacheAsync()
    {
        if (cacheService == null) return null;

        var cached = await cacheService.GetMessagesAsync(chatId, AppConstants.DefaultPageSize);
        if (cached is not { Messages.Count: > 0 }) return null;

        RenderMessages(cached.Messages);
        _hasMoreOlder = cached.HasMoreOlder;
        _hasMoreNewer = false;

        Debug.WriteLine($"[MessageManager] Загружено {cached.Messages.Count} из кеша для чата {chatId}");

        RunInBackground(() => RevalidateNewestAsync(_disposeCts.Token));
        return LastIndexOrNull();
    }

    private async Task<int?> LoadInitialFromServerAsync(CancellationToken ct)
    {
        var url = ApiEndpoints.Messages.ForChat(chatId, userId, 1, AppConstants.DefaultPageSize);
        var data = await FetchAsync(url, ct);
        if (data == null) return null;

        RenderMessages(data.Messages);
        _hasMoreOlder = data.HasMoreMessages;
        _hasMoreNewer = false;

        await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, false);
        return LastIndexOrNull();
    }

    public async Task<int?> LoadMessagesAroundAsync(int messageId, CancellationToken ct = default)
    {
        if (!TryBeginLoading()) return null;

        try { return await LoadAroundCoreAsync(messageId, ct); }
        finally { EndLoading(); }
    }

    private async Task<int?> LoadAroundCoreAsync(int messageId, CancellationToken ct)
    {
        if (cacheService != null)
        {
            var cached = await cacheService.GetMessagesAroundAsync(
                chatId, messageId, AppConstants.DefaultPageSize);

            if (cached is { IsComplete: true, Messages.Count: > 0 })
            {
                RenderMessages(cached.Messages);
                _hasMoreOlder = cached.HasMoreOlder;
                _hasMoreNewer = cached.HasMoreNewer;
                return FindIndexById(messageId);
            }
        }

        var data = await FetchAsync(ApiEndpoints.Messages.Around(chatId, messageId, userId, AppConstants.DefaultPageSize), ct);
        if (data == null) return null;

        RenderMessages(data.Messages);
        _hasMoreOlder = data.HasMoreMessages;
        _hasMoreNewer = data.HasNewerMessages;

        await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, data.HasNewerMessages);
        return FindIndexById(messageId);
    }

    private enum LoadDirection { Older, Newer }

    public Task LoadOlderMessagesAsync(CancellationToken ct = default)
        => LoadPageAsync(LoadDirection.Older, ct);

    public Task LoadNewerMessagesAsync(CancellationToken ct = default)
        => LoadPageAsync(LoadDirection.Newer, ct);

    private async Task LoadPageAsync(LoadDirection direction, CancellationToken ct)
    {
        if (!TryBeginLoading()) return;

        try
        {
            if (!GetHasMore(direction) || GetAnchor(direction) is not { } anchorId)
                return;

            var page = await LoadDirectionalAsync(
                direction, anchorId, AppConstants.LoadMorePageSize, ct);
            if (page is not { Messages.Count: > 0 }) return;

            if (direction == LoadDirection.Older)
            {
                PrependNewMessages(page.Messages);
                _hasMoreOlder = page.HasMore;
            }
            else
            {
                AppendNewMessages(page.Messages);
                _hasMoreNewer = page.HasMore;
            }

            await SafeUpdateSyncStateAsync();
        }
        finally { EndLoading(); }
    }

    private async Task<DirectionalPage?> LoadDirectionalAsync(LoadDirection direction, int anchorId, int count, CancellationToken ct)
    {
        if (cacheService != null)
        {
            var fromCache = await TryLoadDirectionalFromCacheAsync(
                direction, anchorId, count, ct);
            if (fromCache != null)
                return fromCache;
        }

        return await LoadDirectionalFromServerAsync(direction, anchorId, count, ct);
    }

    private async Task<DirectionalPage?> TryLoadDirectionalFromCacheAsync(LoadDirection direction, int anchorId, int count, CancellationToken ct)
    {
        var cached = direction == LoadDirection.Older ? await cacheService!.GetMessagesBeforeAsync(chatId, anchorId, count)
            : await cacheService!.GetMessagesAfterAsync(chatId, anchorId, count);

        if (cached is { IsComplete: true, Messages.Count: > 0 })
            return new DirectionalPage(cached.Messages, GetCachedHasMore(cached, direction));

        if (direction == LoadDirection.Older && cached?.Messages is { Count: > 0 } partial)
            return await BackfillOlderFromServerAsync(partial, count, ct);

        return null;
    }

    private async Task<DirectionalPage?> BackfillOlderFromServerAsync(List<MessageDto> partial, int totalCount, CancellationToken ct)
    {
        var serverAnchor = partial.Min(m => m.Id);
        var remaining = totalCount - partial.Count;

        var url = ApiEndpoints.Messages.Before(chatId, serverAnchor, userId, remaining);
        var data = await FetchAsync(url, ct);
        if (data == null) return null;

        await SafeCacheMessagesAsync(data.Messages);
        return new DirectionalPage(MergeAndDeduplicate(partial, data.Messages), data.HasMoreMessages);
    }

    private async Task<DirectionalPage?> LoadDirectionalFromServerAsync(
        LoadDirection direction, int anchorId, int count, CancellationToken ct)
    {
        var url = BuildDirectionalUrl(direction, anchorId, count);
        var data = await FetchAsync(url, ct);
        if (data == null) return null;

        await SafeCacheMessagesAsync(data.Messages);
        return new DirectionalPage(data.Messages, GetServerHasMore(data, direction));
    }

    private sealed record DirectionalPage(List<MessageDto> Messages, bool HasMore);

    public async Task GapFillAfterReconnectAsync(CancellationToken ct = default)
    {
        if (_newestLoadedMessageId == null || !TryBeginLoading()) return;

        var batchesLoaded = 0;
        var totalAdded = 0;

        try
        {
            while (batchesLoaded < MaxGapFillBatches)
            {
                ct.ThrowIfCancellationRequested();

                var url = ApiEndpoints.Messages.After(
                    chatId, _newestLoadedMessageId!.Value, userId, AppConstants.DefaultPageSize);
                var data = await FetchAsync(url, ct);
                if (data is not { Messages.Count: > 0 }) break;

                batchesLoaded++;
                await SafeCacheMessagesAsync(data.Messages);

                var batch = data;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    totalAdded += AppendNewMessagesAndCount(batch.Messages);
                    _hasMoreNewer = batch.HasNewerMessages;
                });

                await SafeUpdateSyncStateAsync();

                if (!data.HasNewerMessages || data.Messages.Count < AppConstants.DefaultPageSize)
                    break;
            }

            if (batchesLoaded >= MaxGapFillBatches)
            {
                Debug.WriteLine(
                    $"[MessageManager] Лимит GapFill ({MaxGapFillBatches} батчей, {totalAdded} сообщений). Сброс.");
                await ResetToLatestAsync(ct);
            }
            else
            {
                Debug.WriteLine(
                    $"[MessageManager] GapFill завершён: {totalAdded} сообщений за {batchesLoaded} батчей");
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка GapFill: {ex.Message}");
        }
        finally { EndLoading(); }
    }

    private async Task ResetToLatestAsync(CancellationToken ct)
    {
        try
        {
            if (cacheService != null)
                await cacheService.ClearChatMessagesAsync(chatId);

            await Dispatcher.UIThread.InvokeAsync(ClearAllState);

            var url = ApiEndpoints.Messages.ForChat(chatId, userId, 1, AppConstants.DefaultPageSize);
            var data = await FetchAsync(url, ct);
            if (data == null) return;

            var d = data;
            await Dispatcher.UIThread.InvokeAsync(() => ApplyResetData(d));

            await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка сброса: {ex.Message}");
        }
    }

    private void ClearAllState()
    {
        DisposeAllMessages();
        Messages.Clear();
        _loadedMessageIds.Clear();
        _oldestLoadedMessageId = null;
        _newestLoadedMessageId = null;
        _hasMoreOlder = true;
        _hasMoreNewer = false;
    }

    private void ApplyResetData(PagedMessagesDto data)
    {
        RenderMessages(data.Messages);
        _hasMoreOlder = data.HasMoreMessages;
        _hasMoreNewer = false;
    }

    private async Task RevalidateNewestAsync(CancellationToken ct)
    {
        if (_newestLoadedMessageId == null) return;

        try
        {
            var url = ApiEndpoints.Messages.After(
                chatId, _newestLoadedMessageId.Value, userId, AppConstants.DefaultPageSize);
            var data = await FetchAsync(url, ct);
            if (data is not { Messages.Count: > 0 }) return;

            await SafeCacheMessagesAsync(data.Messages);

            var d = data;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppendNewMessages(d.Messages);
                _hasMoreNewer = d.HasNewerMessages;
            });

            await SafeUpdateSyncStateAsync();
        }
        catch (OperationCanceledException) { /* закрытие чата */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка ревалидации: {ex.Message}");
        }
    }

    #endregion

    #region Real-time Event Handling

    public void AddReceivedMessage(MessageDto message)
    {
        if (!_loadedMessageIds.Add(message.Id)) return;

        var vm = CreateMessageViewModel(message);
        if (message.SenderId != userId)
            vm.IsUnread = true;

        Messages.Add(vm);
        TrackBounds(message.Id);
        UpdateDateSeparatorForNewMessage(vm);
        MessageViewModel.UpdateGroupingAround(Messages, Messages.Count - 1);
        TrimOldMessagesFromStart();

        _hasMoreNewer = false;

        RunInBackground(async () =>
        {
            if (cacheService == null) return;
            await SafeCacheAsync(() => cacheService.UpsertMessageAsync(message));
            await SafeUpdateSyncStateAsync();
        });
    }

    public void HandleMessageDeleted(int messageId)
    {
        var msg = Messages.FirstOrDefault(m => m.Id == messageId);
        if (msg == null) return;

        var index = Messages.IndexOf(msg);
        msg.MarkAsDeleted();
        MessageViewModel.UpdateGroupingAround(Messages, index);

        RunInBackground(() =>
        {
            if (cacheService == null) return Task.CompletedTask;
            return SafeCacheAsync(() => cacheService.MarkMessageDeletedAsync(messageId));
        });
    }

    public void HandleMessageUpdated(MessageDto updatedDto)
    {
        var msg = Messages.FirstOrDefault(m => m.Id == updatedDto.Id);
        if (msg == null) return;

        msg.ApplyUpdate(updatedDto);

        RunInBackground(() =>
        {
            if (cacheService == null) return Task.CompletedTask;
            return SafeCacheAsync(() => cacheService.UpsertMessageAsync(updatedDto));
        });
    }

    #endregion

    #region Read Status

    public void MarkAsReadLocally(int messageId)
    {
        foreach (var msg in Messages.Where(m => m.Id <= messageId && m.IsUnread))
            msg.IsUnread = false;

        if (!LastReadMessageId.HasValue || messageId > LastReadMessageId.Value)
            LastReadMessageId = messageId;
    }

    public IEnumerable<MessageViewModel> GetUnreadMessages()
        => Messages.Where(m => m.IsUnread && m.SenderId != userId);

    public int GetPollsCount()
        => Messages.Count(m => m.Poll != null);

    #endregion

    #region UI Collection

    private void AppendNewMessages(List<MessageDto> dtos)
    {
        var lookup = BuildMembersLookup();
        var startIndex = Messages.Count;
        var added = false;

        foreach (var msg in dtos)
        {
            if (!_loadedMessageIds.Add(msg.Id)) continue;
            Messages.Add(CreateMessageViewModel(msg, lookup));
            TrackBounds(msg.Id);
            added = true;
        }

        if (added)
        {
            TrimOldMessagesFromStart();
            UpdateDateSeparators();
            UpdateGroupingFrom(Math.Max(0, startIndex - 1));
        }
    }

    private int AppendNewMessagesAndCount(List<MessageDto> dtos)
    {
        var before = Messages.Count;
        AppendNewMessages(dtos);
        return Messages.Count - before;
    }

    private void PrependNewMessages(List<MessageDto> dtos)
    {
        var lookup = BuildMembersLookup();
        var added = false;

        for (var i = dtos.Count - 1; i >= 0; i--)
        {
            var msg = dtos[i];
            if (!_loadedMessageIds.Add(msg.Id)) continue;
            Messages.Insert(0, CreateMessageViewModel(msg, lookup));
            TrackBounds(msg.Id);
            added = true;
        }

        if (added)
        {
            RecalculateGrouping();
            UpdateDateSeparators();
            RecalculateGrouping();
        }
    }

    private void RenderMessages(List<MessageDto> messages)
    {
        DisposeAllMessages();
        Messages.Clear();
        _loadedMessageIds.Clear();
        _oldestLoadedMessageId = null;
        _newestLoadedMessageId = null;

        var lookup = BuildMembersLookup();

        foreach (var msg in messages)
        {
            if (!_loadedMessageIds.Add(msg.Id)) continue;
            Messages.Add(CreateMessageViewModel(msg, lookup));
            TrackBounds(msg.Id);
        }

        UpdateDateSeparators();
        RecalculateGrouping();
    }

    private void DisposeAllMessages()
    {
        foreach (var msg in Messages)
        {
            if (msg is IDisposable disposableMsg)
                disposableMsg.Dispose();

            DisposeMessage(msg);
        }
    }
    private void TrimOldMessagesFromStart()
    {
        if (Messages.Count <= MaxMessagesInMemory)
            return;

        var removeCount = Math.Min(TrimBatchSize, Messages.Count - MaxMessagesInMemory);
        if (removeCount <= 0)
            return;

        for (int i = 0; i < removeCount; i++)
        {
            var message = Messages[0];
            _loadedMessageIds.Remove(message.Id);
            DisposeMessage(message);
            Messages.RemoveAt(0);
        }
        _hasMoreOlder = true;
        RecalculateBounds();
    }

    private static void DisposeMessage(MessageViewModel message)
    {
        foreach (var file in message.FileViewModels)
        {
            if (file is IDisposable disposable)
                disposable.Dispose();
        }

        message.Dispose();
    }

    private MessageViewModel CreateMessageViewModel(MessageDto msg, Dictionary<int, UserDto>? lookup = null)
    {
        lookup ??= BuildMembersLookup();
        lookup.TryGetValue(msg.SenderId, out var sender);

        var vm = new MessageViewModel(msg, downloadService, notificationService, _audioPlayer, _apiClient)
        {
            SenderName = sender?.DisplayName ?? sender?.Username ?? msg.SenderName ?? "Unknown",
            SenderAvatar = sender?.Avatar ?? msg.SenderAvatarUrl
        };

        if (LastReadMessageId.HasValue && msg.Id > LastReadMessageId.Value && msg.SenderId != userId)
            vm.IsUnread = true;

        return vm;
    }

    private Dictionary<int, UserDto> BuildMembersLookup()
        => _getMembersFunc().ToDictionary(m => m.Id);

    #endregion

    #region API & Cache

    private async Task<PagedMessagesDto?> FetchAsync(string url, CancellationToken ct)
    {
        var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);
        return result is { Success: true, Data: not null } ? result.Data : null;
    }

    private async Task SafeCacheAsync(Func<Task> action)
    {
        if (cacheService == null) return;

        try { await action(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка кеша: {ex.Message}");
        }
    }

    private Task SafeCacheMessagesAsync(List<MessageDto> messages)
        => messages.Count > 0 ? SafeCacheAsync(() => cacheService!.UpsertMessagesAsync(messages)) : Task.CompletedTask;

    private Task SafeUpdateSyncStateAsync()
        => SafeCacheAsync(() => cacheService!.UpdateSyncStateAsync(BuildSyncState()));

    private async Task SafeSaveToCacheAsync(List<MessageDto> messages, bool hasMoreOlder, bool hasMoreNewer)
    {
        if (cacheService == null || messages.Count == 0) return;
        await SafeCacheAsync(() => SaveToCacheCoreAsync(messages, hasMoreOlder, hasMoreNewer));
    }

    private async Task SaveToCacheCoreAsync(List<MessageDto> messages, bool hasMoreOlder, bool hasMoreNewer)
    {
        await cacheService!.UpsertMessagesAsync(messages);
        await cacheService.UpdateSyncStateAsync(BuildSyncState(hasMoreOlder, hasMoreNewer));
    }

    private ChatSyncState BuildSyncState(bool? hasMoreOlder = null, bool? hasMoreNewer = null) => new()
    {
        ChatId = chatId,
        OldestLoadedId = _oldestLoadedMessageId,
        NewestLoadedId = _newestLoadedMessageId,
        HasMoreOlder = hasMoreOlder ?? _hasMoreOlder,
        HasMoreNewer = hasMoreNewer ?? _hasMoreNewer
    };

    #endregion

    #region Direction Helpers

    private int? GetAnchor(LoadDirection direction)
        => direction == LoadDirection.Older ? _oldestLoadedMessageId : _newestLoadedMessageId;

    private bool GetHasMore(LoadDirection direction)
        => direction == LoadDirection.Older ? _hasMoreOlder : _hasMoreNewer;

    private string BuildDirectionalUrl(LoadDirection direction, int anchorId, int count)
        => direction == LoadDirection.Older ? ApiEndpoints.Messages.Before(chatId, anchorId, userId, count)
            : ApiEndpoints.Messages.After(chatId, anchorId, userId, count);

    private static bool GetCachedHasMore(CachedMessagesResult cached, LoadDirection direction)
        => direction == LoadDirection.Older ? cached.HasMoreOlder : cached.HasMoreNewer;

    private static bool GetServerHasMore(PagedMessagesDto data, LoadDirection direction)
        => direction == LoadDirection.Older ? data.HasMoreMessages : data.HasNewerMessages;

    #endregion

    #region Grouping

    private void RecalculateGrouping()
        => MessageViewModel.RecalculateGrouping(Messages);

    private void UpdateGroupingFrom(int startIndex)
    {
        for (var i = startIndex; i < Messages.Count; i++)
        {
            var current = Messages[i];
            var prev = i > 0 ? Messages[i - 1] : null;
            var next = i < Messages.Count - 1 ? Messages[i + 1] : null;

            current.IsContinuation = prev != null && MessageViewModel.CanGroup(prev, current);
            current.HasNextFromSame = next != null && MessageViewModel.CanGroup(current, next);
        }
    }

    #endregion

    #region Bounds & Date Separators

    private void TrackBounds(int id)
    {
        _oldestLoadedMessageId = _oldestLoadedMessageId.HasValue ? Math.Min(_oldestLoadedMessageId.Value, id) : id;
        _newestLoadedMessageId = _newestLoadedMessageId.HasValue ? Math.Max(_newestLoadedMessageId.Value, id) : id;
    }
    private void RecalculateBounds()
    {
        if (Messages.Count == 0)
        {
            _oldestLoadedMessageId = null;
            _newestLoadedMessageId = null;
            return;
        }

        _oldestLoadedMessageId = Messages.Min(m => m.Id);
        _newestLoadedMessageId = Messages.Max(m => m.Id);
    }

    private void UpdateDateSeparators()
    {
        DateTime? previousDate = null;

        foreach (var message in Messages)
        {
            var messageDate = message.CreatedAt.Date;
            var isNewDate = previousDate == null || messageDate != previousDate.Value;

            message.ShowDateSeparator = isNewDate;
            message.DateSeparatorText = isNewDate ? FormatDateSeparator(messageDate) : null;

            previousDate = messageDate;
        }
    }

    private void UpdateDateSeparatorForNewMessage(MessageViewModel newMessage)
    {
        var messageDate = newMessage.CreatedAt.Date;
        var index = Messages.IndexOf(newMessage);

        if (index <= 0)
        {
            newMessage.ShowDateSeparator = true;
            newMessage.DateSeparatorText = FormatDateSeparator(messageDate);
            return;
        }

        var previousDate = Messages[index - 1].CreatedAt.Date;
        newMessage.ShowDateSeparator = messageDate != previousDate;
        newMessage.DateSeparatorText = newMessage.ShowDateSeparator ? FormatDateSeparator(messageDate) : null;
    }

    private static string FormatDateSeparator(DateTime date)
    {
        var today = DateTime.Today;

        if (date == today) return "Сегодня";
        if (date == today.AddDays(-1)) return "Вчера";

        var culture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        return date.Year == today.Year ? date.ToString("d MMMM", culture) : date.ToString("d MMMM yyyy", culture);
    }

    #endregion

    #region Helpers

    private int? LastIndexOrNull()
        => Messages.Count > 0 ? Messages.Count - 1 : null;

    private int? FindIndexById(int id)
    {
        for (var i = 0; i < Messages.Count; i++)
            if (Messages[i].Id == id) return i;
        return null;
    }

    private static List<MessageDto> MergeAndDeduplicate(List<MessageDto>? first, List<MessageDto> second)
        => [.. (first ?? []).Concat(second).GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.Id)];

    private void RunInBackground(Func<Task> action, [CallerMemberName] string? caller = null)
    {
        var token = _disposeCts.Token;

        var task = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await action();
            }
            catch (OperationCanceledException) { /* dispose */ }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageManager] Ошибка фоновой задачи в {caller}: {ex.Message}");
            }
        }, token);

        lock (_bgLock)
        {
            _backgroundTasks.RemoveAll(t => t.IsCompleted);
            _backgroundTasks.Add(task);
        }
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();

        Task[] pending;
        lock (_bgLock) pending = [.. _backgroundTasks];

        try { await Task.WhenAll(pending); }
        catch { /* expected */ }

        DisposeAllMessages();
        _disposeCts.Dispose();
    }

    #endregion
}