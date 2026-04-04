using MessengerDesktop.Data.Entities;
using MessengerDesktop.Data.Repositories;
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

public sealed class ChatMessageManager(int chatId, int userId, IApiClientService apiClient, Func<ObservableCollection<UserDto>> getMembersFunc,
    IFileDownloadService? downloadService = null, INotificationService? notificationService = null, ILocalCacheService? cacheService = null,
    IAudioPlayerService? audioPlayer = null) : IAsyncDisposable
{
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
    private const int DefaultPage = AppConstants.DefaultPageSize;
    private const int LoadMorePage = AppConstants.LoadMorePageSize;

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public bool IsLoading => Volatile.Read(ref _isLoading) != 0;
    public bool HasMoreOlder => _hasMoreOlder;
    public bool HasMoreNewer => _hasMoreNewer;
    public int? LastReadMessageId { get; private set; }
    public int? FirstUnreadMessageId { get; private set; }

    private bool TryBeginLoading() => Interlocked.CompareExchange(ref _isLoading, 1, 0) == 0;
    private void EndLoading() => Interlocked.Exchange(ref _isLoading, 0);

    public void SetReadInfo(ChatReadInfoDto? info)
    {
        if (info == null) return;
        LastReadMessageId = info.LastReadMessageId;
        FirstUnreadMessageId = info.FirstUnreadMessageId;
        Debug.WriteLine($"[MessageManager] ReadInfo: lastRead={LastReadMessageId}, firstUnread={FirstUnreadMessageId}");
    }

    public Task<int?> LoadInitialMessagesAsync(CancellationToken ct = default)
        => WithLoadingGuardAsync(() => LoadInitialCoreAsync(ct));

    public Task<int?> LoadMessagesAroundAsync(int messageId, CancellationToken ct = default)
        => WithLoadingGuardAsync(() => LoadAroundCoreAsync(messageId, ct));

    public Task LoadOlderMessagesAsync(CancellationToken ct = default)
        => WithLoadingGuardVoidAsync(() => LoadPageAsync(LoadDirection.Older, ct));

    public Task LoadNewerMessagesAsync(CancellationToken ct = default)
        => WithLoadingGuardVoidAsync(() => LoadPageAsync(LoadDirection.Newer, ct));

    private async Task<int?> LoadInitialCoreAsync(CancellationToken ct)
    {
        if (FirstUnreadMessageId.HasValue)
            return await LoadAroundCoreAsync(FirstUnreadMessageId.Value, ct);

        if (await TryLoadInitialFromCacheAsync() is { } cachedIndex)
            return cachedIndex;

        return await LoadInitialFromServerAsync(ct);
    }

    private async Task<int?> TryLoadInitialFromCacheAsync()
    {
        if (cacheService == null) return null;

        var cached = await cacheService.GetMessagesAsync(chatId, DefaultPage);
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
        var data = await FetchAsync(ApiEndpoints.Messages.ForChat(chatId, userId, 1, DefaultPage), ct);
        if (data == null) return null;

        RenderMessages(data.Messages);
        SetBounds(data.HasMoreMessages, false);
        await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, false);
        return LastIndexOrNull();
    }

    private async Task<int?> LoadAroundCoreAsync(int messageId, CancellationToken ct)
    {
        if (cacheService != null)
        {
            var cached = await cacheService.GetMessagesAroundAsync(chatId, messageId, DefaultPage);
            if (cached is { IsComplete: true, Messages.Count: > 0 })
            {
                RenderMessages(cached.Messages);
                SetBounds(cached.HasMoreOlder, cached.HasMoreNewer);
                return FindIndexById(messageId);
            }
        }

        var data = await FetchAsync(ApiEndpoints.Messages.Around(chatId, messageId, userId, DefaultPage), ct);
        if (data == null) return null;

        RenderMessages(data.Messages);
        SetBounds(data.HasMoreMessages, data.HasNewerMessages);
        await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, data.HasNewerMessages);
        return FindIndexById(messageId);
    }

    private async Task LoadPageAsync(LoadDirection direction, CancellationToken ct)
    {
        if (!GetHasMore(direction) || GetAnchor(direction) is not { } anchorId)
            return;

        var page = await LoadDirectionalAsync(direction, anchorId, LoadMorePage, ct);
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

    private async Task<DirectionalPage?> LoadDirectionalAsync(LoadDirection dir, int anchorId, int count, CancellationToken ct)
    {
        if (cacheService != null)
        {
            var fromCache = await TryLoadDirectionalFromCacheAsync(dir, anchorId, count, ct);
            if (fromCache != null) return fromCache;
        }
        return await LoadDirectionalFromServerAsync(dir, anchorId, count, ct);
    }

    private async Task<DirectionalPage?> TryLoadDirectionalFromCacheAsync(LoadDirection dir, int anchorId, int count, CancellationToken ct)
    {
        var cached = dir == LoadDirection.Older
            ? await cacheService!.GetMessagesBeforeAsync(chatId, anchorId, count)
            : await cacheService!.GetMessagesAfterAsync(chatId, anchorId, count);

        if (cached is { IsComplete: true, Messages.Count: > 0 })
            return new(cached.Messages, GetCachedHasMore(cached, dir));

        if (dir == LoadDirection.Older && cached?.Messages is { Count: > 0 } partial)
        {
            var serverAnchor = partial.Min(m => m.Id);
            var data = await FetchAsync(
                ApiEndpoints.Messages.Before(chatId, serverAnchor, userId, count - partial.Count), ct);
            if (data == null) return null;

            await SafeCacheMessagesAsync(data.Messages);
            return new(MergeAndDeduplicate(partial, data.Messages), data.HasMoreMessages);
        }

        return null;
    }

    private async Task<DirectionalPage?> LoadDirectionalFromServerAsync(LoadDirection dir, int anchorId, int count, CancellationToken ct)
    {
        var data = await FetchAsync(BuildDirectionalUrl(dir, anchorId, count), ct);
        if (data == null) return null;

        await SafeCacheMessagesAsync(data.Messages);
        return new(data.Messages, GetServerHasMore(data, dir));
    }

    public async Task GapFillAfterReconnectAsync(CancellationToken ct = default)
    {
        if (_newestLoadedMessageId == null || !TryBeginLoading()) return;

        try
        {
            var (batches, totalAdded) = await GapFillLoopAsync(ct);

            if (batches >= MaxGapFillBatches)
            {
                Debug.WriteLine($"[MessageManager] Лимит GapFill ({MaxGapFillBatches} батчей, {totalAdded} сообщений). Сброс.");
                await ResetToLatestAsync(ct);
            }
            else
            {
                Debug.WriteLine($"[MessageManager] GapFill завершён: {totalAdded} сообщений за {batches} батчей");
            }
        }
        catch (OperationCanceledException) { /* Отменено */ }
        catch (Exception ex) { Debug.WriteLine($"[MessageManager] Ошибка GapFill: {ex.Message}"); }
        finally { EndLoading(); }
    }

    private async Task<(int batches, int totalAdded)> GapFillLoopAsync(CancellationToken ct)
    {
        int batches = 0, totalAdded = 0;

        while (batches < MaxGapFillBatches)
        {
            ct.ThrowIfCancellationRequested();

            var url = ApiEndpoints.Messages.After(chatId, _newestLoadedMessageId!.Value, userId, DefaultPage);
            var data = await FetchAsync(url, ct);
            if (data is not { Messages.Count: > 0 }) break;

            batches++;
            await SafeCacheMessagesAsync(data.Messages);

            var d = data;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                totalAdded += AppendAndCount(d.Messages);
                _hasMoreNewer = d.HasNewerMessages;
            });

            await SafeUpdateSyncStateAsync();

            if (!data.HasNewerMessages || data.Messages.Count < DefaultPage)
                break;
        }

        return (batches, totalAdded);
    }

    private async Task ResetToLatestAsync(CancellationToken ct)
    {
        try
        {
            if (cacheService != null)
                await cacheService.ClearChatMessagesAsync(chatId);

            await Dispatcher.UIThread.InvokeAsync(ClearAllState);

            var data = await FetchAsync(ApiEndpoints.Messages.ForChat(chatId, userId, 1, DefaultPage), ct);
            if (data == null) return;

            var d = data;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RenderMessages(d.Messages);
                SetBounds(d.HasMoreMessages, false);
            });

            await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, false);
        }
        catch (Exception ex) { Debug.WriteLine($"[MessageManager] Ошибка сброса: {ex.Message}"); }
    }

    private async Task RevalidateNewestAsync(CancellationToken ct)
    {
        if (_newestLoadedMessageId == null) return;

        try
        {
            var url = ApiEndpoints.Messages.After(chatId, _newestLoadedMessageId.Value, userId, DefaultPage);
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
        catch (OperationCanceledException) { /* Отменено */ }
        catch (Exception ex) { Debug.WriteLine($"[MessageManager] Ошибка ревалидации: {ex.Message}"); }
    }

    public void AddReceivedMessage(MessageDto message)
    {
        if (!_loadedMessageIds.Add(message.Id)) return;

        var vm = CreateMessageViewModel(message);
        if (message.SenderId != userId) vm.IsUnread = true;

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
        var msg = FindMessage(messageId);
        if (msg == null) return;

        msg.MarkAsDeleted();
        MessageViewModel.UpdateGroupingAround(Messages, Messages.IndexOf(msg));

        RunInBackground(() => SafeCacheIfAvailable(() => cacheService!.MarkMessageDeletedAsync(messageId)));
    }

    public void HandleMessageUpdated(MessageDto updatedDto)
    {
        FindMessage(updatedDto.Id)?.ApplyUpdate(updatedDto);
        RunInBackground(() => SafeCacheIfAvailable(() => cacheService!.UpsertMessageAsync(updatedDto)));
    }

    public void MarkAsReadLocally(int messageId)
    {
        foreach (var msg in Messages.Where(m => m.Id <= messageId && m.IsUnread))
            msg.IsUnread = false;

        if (!LastReadMessageId.HasValue || messageId > LastReadMessageId.Value)
            LastReadMessageId = messageId;
    }

    public IEnumerable<MessageViewModel> GetUnreadMessages()
        => Messages.Where(m => m.IsUnread && m.SenderId != userId);

    public int GetPollsCount() => Messages.Count(m => m.Poll != null);

    private void AppendNewMessages(List<MessageDto> dtos) => MutateMessages(dtos, append: true);
    private void PrependNewMessages(List<MessageDto> dtos) => MutateMessages(dtos, append: false);

    private void MutateMessages(List<MessageDto> dtos, bool append)
    {
        var lookup = BuildMembersLookup();
        var startIndex = Messages.Count;
        var added = false;

        var ordered = append ? dtos : dtos.AsEnumerable().Reverse();

        foreach (var msg in ordered)
        {
            if (!_loadedMessageIds.Add(msg.Id)) continue;

            var vm = CreateMessageViewModel(msg, lookup);
            if (append) Messages.Add(vm);
            else Messages.Insert(0, vm);

            TrackBounds(msg.Id);
            added = true;
        }

        if (!added) return;

        UpdateDateSeparators();

        if (append)
        {
            TrimOldMessagesFromStart();
            UpdateGroupingFrom(Math.Max(0, startIndex - 1));
        }
        else
        {
            RecalculateGrouping();
        }
    }

    private int AppendAndCount(List<MessageDto> dtos)
    {
        var before = Messages.Count;
        AppendNewMessages(dtos);
        return Messages.Count - before;
    }

    private void RenderMessages(List<MessageDto> messages)
    {
        ClearAllState();
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

    private void TrimOldMessagesFromStart()
    {
        if (Messages.Count <= MaxMessagesInMemory) return;

        var removeCount = Math.Min(TrimBatchSize, Messages.Count - MaxMessagesInMemory);
        for (var i = 0; i < removeCount; i++)
        {
            var msg = Messages[0];
            _loadedMessageIds.Remove(msg.Id);
            DisposeMessage(msg);
            Messages.RemoveAt(0);
        }

        _hasMoreOlder = true;
        RecalculateBounds();
    }

    private MessageViewModel CreateMessageViewModel(MessageDto msg, Dictionary<int, UserDto>? lookup = null)
    {
        lookup ??= BuildMembersLookup();
        lookup.TryGetValue(msg.SenderId, out var sender);

        var vm = new MessageViewModel(msg, downloadService, notificationService, audioPlayer, _apiClient)
        {
            SenderName = sender?.DisplayName ?? sender?.Username ?? msg.SenderName ?? "Unknown",
            SenderAvatar = sender?.Avatar ?? msg.SenderAvatarUrl
        };

        if (LastReadMessageId.HasValue && msg.Id > LastReadMessageId.Value && msg.SenderId != userId)
            vm.IsUnread = true;

        return vm;
    }

    private Dictionary<int, UserDto> BuildMembersLookup() => _getMembersFunc().ToDictionary(m => m.Id);

    private void DisposeAllMessages()
    {
        foreach (var msg in Messages)
            DisposeMessage(msg);
    }

    private static void DisposeMessage(MessageViewModel msg)
    {
        foreach (var file in msg.FileViewModels)
            (file as IDisposable)?.Dispose();
        msg.Dispose();
    }

    private async Task<PagedMessagesDto?> FetchAsync(string url, CancellationToken ct)
    {
        var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);
        return result is { Success: true, Data: not null } ? result.Data : null;
    }

    private static async Task SafeCacheAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Debug.WriteLine($"[MessageManager] Ошибка кеша: {ex.Message}"); }
    }

    private Task SafeCacheIfAvailable(Func<Task> action)
        => cacheService != null ? SafeCacheAsync(action) : Task.CompletedTask;

    private Task SafeCacheMessagesAsync(List<MessageDto> msgs)
        => msgs.Count > 0 ? SafeCacheIfAvailable(() => cacheService!.UpsertMessagesAsync(msgs)) : Task.CompletedTask;

    private Task SafeUpdateSyncStateAsync()
        => SafeCacheIfAvailable(() => cacheService!.UpdateSyncStateAsync(BuildSyncState()));

    private async Task SafeSaveToCacheAsync(List<MessageDto> msgs, bool hasOlder, bool hasNewer)
    {
        if (cacheService == null || msgs.Count == 0) return;
        await SafeCacheAsync(async () =>
        {
            await cacheService.UpsertMessagesAsync(msgs);
            await cacheService.UpdateSyncStateAsync(BuildSyncState(hasOlder, hasNewer));
        });
    }

    private ChatSyncState BuildSyncState(bool? hasOlder = null, bool? hasNewer = null) => new()
    {
        ChatId = chatId,
        OldestLoadedId = _oldestLoadedMessageId,
        NewestLoadedId = _newestLoadedMessageId,
        HasMoreOlder = hasOlder ?? _hasMoreOlder,
        HasMoreNewer = hasNewer ?? _hasMoreNewer
    };

    private enum LoadDirection { Older, Newer }
    private sealed record DirectionalPage(List<MessageDto> Messages, bool HasMore);

    private int? GetAnchor(LoadDirection d) => d == LoadDirection.Older ? _oldestLoadedMessageId : _newestLoadedMessageId;
    private bool GetHasMore(LoadDirection d) => d == LoadDirection.Older ? _hasMoreOlder : _hasMoreNewer;

    private string BuildDirectionalUrl(LoadDirection d, int anchor, int count) => d == LoadDirection.Older
        ? ApiEndpoints.Messages.Before(chatId, anchor, userId, count)
        : ApiEndpoints.Messages.After(chatId, anchor, userId, count);

    private static bool GetCachedHasMore(CachedMessagesResult c, LoadDirection d) => d == LoadDirection.Older ? c.HasMoreOlder : c.HasMoreNewer;
    private static bool GetServerHasMore(PagedMessagesDto p, LoadDirection d) => d == LoadDirection.Older ? p.HasMoreMessages : p.HasNewerMessages;

    private void SetBounds(bool hasOlder, bool hasNewer) { _hasMoreOlder = hasOlder; _hasMoreNewer = hasNewer; }

    private void RecalculateGrouping() => MessageViewModel.RecalculateGrouping(Messages);

    private void UpdateGroupingFrom(int startIndex)
    {
        for (var i = startIndex; i < Messages.Count; i++)
        {
            var cur = Messages[i];
            var prev = i > 0 ? Messages[i - 1] : null;
            var next = i < Messages.Count - 1 ? Messages[i + 1] : null;
            cur.IsContinuation = prev != null && MessageViewModel.CanGroup(prev, cur);
            cur.HasNextFromSame = next != null && MessageViewModel.CanGroup(cur, next);
        }
    }

    private void TrackBounds(int id)
    {
        _oldestLoadedMessageId = _oldestLoadedMessageId.HasValue ? Math.Min(_oldestLoadedMessageId.Value, id) : id;
        _newestLoadedMessageId = _newestLoadedMessageId.HasValue ? Math.Max(_newestLoadedMessageId.Value, id) : id;
    }

    private void RecalculateBounds()
    {
        if (Messages.Count == 0) { _oldestLoadedMessageId = _newestLoadedMessageId = null; return; }
        _oldestLoadedMessageId = Messages.Min(m => m.Id);
        _newestLoadedMessageId = Messages.Max(m => m.Id);
    }

    private void UpdateDateSeparators()
    {
        DateTime? prev = null;
        foreach (var msg in Messages)
        {
            var date = msg.CreatedAt.Date;
            var isNew = prev == null || date != prev.Value;
            msg.ShowDateSeparator = isNew;
            msg.DateSeparatorText = isNew ? FormatDateSeparator(date) : null;
            prev = date;
        }
    }

    private void UpdateDateSeparatorForNewMessage(MessageViewModel vm)
    {
        var date = vm.CreatedAt.Date;
        var idx = Messages.IndexOf(vm);
        var isNew = idx <= 0 || date != Messages[idx - 1].CreatedAt.Date;
        vm.ShowDateSeparator = isNew;
        vm.DateSeparatorText = isNew ? FormatDateSeparator(date) : null;
    }

    private static string FormatDateSeparator(DateTime date)
    {
        var today = DateTime.Today;
        if (date == today) return "Сегодня";
        if (date == today.AddDays(-1)) return "Вчера";

        var culture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        return date.ToString(date.Year == today.Year ? "d MMMM" : "d MMMM yyyy", culture);
    }

    private MessageViewModel? FindMessage(int id) => Messages.FirstOrDefault(m => m.Id == id);
    private int? LastIndexOrNull() => Messages.Count > 0 ? Messages.Count - 1 : null;

    private int? FindIndexById(int id)
    {
        for (var i = 0; i < Messages.Count; i++)
            if (Messages[i].Id == id) return i;
        return null;
    }

    private static List<MessageDto> MergeAndDeduplicate(List<MessageDto>? first, List<MessageDto> second)
        => [.. (first ?? []).Concat(second).GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.Id)];

    private async Task<int?> WithLoadingGuardAsync(Func<Task<int?>> action)
    {
        if (!TryBeginLoading()) return null;
        try { return await action(); }
        finally { EndLoading(); }
    }

    private async Task WithLoadingGuardVoidAsync(Func<Task> action)
    {
        if (!TryBeginLoading()) return;
        try { await action(); }
        finally { EndLoading(); }
    }

    private void RunInBackground(Func<Task> action, [CallerMemberName] string? caller = null)
    {
        var token = _disposeCts.Token;
        var task = Task.Run(async () =>
        {
            try { token.ThrowIfCancellationRequested(); await action(); }
            catch (OperationCanceledException) { /* Отменено */ }
            catch (Exception ex) { Debug.WriteLine($"[MessageManager] Фоновая ошибка в {caller}: {ex.Message}"); }
        }, token);

        lock (_bgLock)
        {
            _backgroundTasks.RemoveAll(t => t.IsCompleted);
            _backgroundTasks.Add(task);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();

        Task[] pending;
        lock (_bgLock) pending = [.. _backgroundTasks];

        try { await Task.WhenAll(pending); } catch { /* Ignored */ }

        DisposeAllMessages();
        _disposeCts.Dispose();
    }
}