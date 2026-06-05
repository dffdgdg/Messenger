using Desktop.Data.Models.Sync;
using Desktop.Data.Repositories.Abstractions;
using Desktop.Infrastructure;
using Desktop.Services.Features.Media.Files;
using Desktop.ViewModels.Chat.Commands;
using Desktop.ViewModels.Chat.Context;
using Desktop.ViewModels.ChatList.Factories;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Desktop.ViewModels.Chat.Managers;

public sealed class ChatMessageManager(
    ChatContext context,
    MediaServices media,
    ChatCommands? chatCommands = null,
    ICommand? mentionClickCommand = null) : IAsyncDisposable
{
    private readonly IApiClientService _apiClient = context.Api;
    private readonly Func<ObservableCollection<UserDto>> _getMembersFunc = () => context.Members;
    private readonly IFileDownloadService? _downloadService = context.FileDownload;
    private readonly IFileDownloadStateService? _stateService = context.FileDownloadState;
    private readonly INotificationService? _notificationService = context.Notifications;
    private readonly ILocalCacheService? _cacheService = context.Cache;
    private readonly IAudioPlayerService? _audioPlayer = (media ?? throw new ArgumentNullException(nameof(media))).AudioPlayer;
    private readonly int _chatId = context.ChatId;
    private readonly int _userId = context.CurrentUserId;

    private int? _oldestLoadedMessageId;
    private int? _newestLoadedMessageId;
    private volatile bool _hasMoreOlder = true;
    private volatile bool _hasMoreNewer;
    private readonly HashSet<int> _loadedMessageIds = [];
    private readonly Dictionary<int, MessageViewModel> _messageIndex = [];
    private int _isLoading;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Lock _bgLock = new();
    private readonly List<Task> _backgroundTasks = [];
    private Dictionary<int, UserDto>? _membersLookup;

    private const int MaxGapFillBatches = 5;
    private const int MaxMessagesInMemory = 300;
    private const int TrimBatchSize = 100;
    private const int DefaultPage = AppConstants.DefaultPageSize;
    private const int LoadMorePage = AppConstants.LoadMorePageSize;
    private const int CacheRevalidationThresholdSeconds = 30;

    public RangeObservableCollection<MessageViewModel> Messages { get; } = [];
    public bool IsLoading => Volatile.Read(ref _isLoading) != 0;
    public bool HasMoreOlder => _hasMoreOlder;
    public bool HasMoreNewer => _hasMoreNewer;
    public int? LastReadMessageId { get; private set; }
    public int? FirstUnreadMessageId { get; private set; }

    public void SetReadInfo(ChatReadInfoDto? info)
    {
        if (info == null) return;
        LastReadMessageId = info.LastReadMessageId;
        FirstUnreadMessageId = info.FirstUnreadMessageId;
        Debug.WriteLine($"[MessageManager] ReadInfo: lastRead={LastReadMessageId}, firstUnread={FirstUnreadMessageId}");
    }

    public void InvalidateMembersCache() => _membersLookup = null;

    public Task<int?> LoadInitialMessagesAsync(int? targetMessageId = null, CancellationToken ct = default)
        => WithLoadingGuardAsync(() => LoadInitialCoreAsync(targetMessageId, ct));

    public Task<int?> LoadMessagesAroundAsync(int messageId, CancellationToken ct = default)
        => WithLoadingGuardAsync(() => LoadAroundCoreAsync(messageId, ct));

    public Task LoadOlderMessagesAsync(CancellationToken ct = default)
        => WithLoadingGuardVoidAsync(() => LoadPageAsync(LoadDirection.Older, ct));

    public Task LoadNewerMessagesAsync(CancellationToken ct = default)
        => WithLoadingGuardVoidAsync(() => LoadPageAsync(LoadDirection.Newer, ct));

    private async Task<int?> LoadInitialCoreAsync(int? targetMessageId, CancellationToken ct)
    {
        if (targetMessageId.HasValue) return await LoadAroundCoreAsync(targetMessageId.Value, ct);
        if (FirstUnreadMessageId.HasValue) return await LoadAroundCoreAsync(FirstUnreadMessageId.Value, ct);
        if (await TryLoadInitialFromCacheAsync() is { } cachedIndex) return cachedIndex;
        return await LoadInitialFromServerAsync(ct);
    }

    private async Task<int?> TryLoadInitialFromCacheAsync()
    {
        if (_cacheService == null) return null;

        var cached = await Task.Run(() => _cacheService.GetMessagesAsync(_chatId, DefaultPage));
        if (cached is not { Messages.Count: >= DefaultPage / 2 }) return null;

        var syncState = await _cacheService.GetSyncStateAsync(_chatId);

        if (syncState?.HasMoreOlder == false && syncState.OldestLoadedId.HasValue)
        {
            var actualOldest = cached.Messages.Count > 0 ? cached.Messages[0].Id : (int?)null;
            if (actualOldest > syncState.OldestLoadedId.Value)
            {
                Debug.WriteLine($"[MessageManager] Кэш рассинхронизирован: syncState.Oldest={syncState.OldestLoadedId}, actual={actualOldest}");
                Debug.WriteLine("[MessageManager] Очищаем кэш и грузим с сервера");

                // Очищаем рассинхронизированный кэш
                try { await _cacheService.ClearChatMessagesAsync(_chatId); } catch { }

                // Грузим с сервера
                return await LoadInitialFromServerAsync(CancellationToken.None);
            }
        }

        RenderMessages(cached.Messages);
        _hasMoreOlder = cached.HasMoreOlder;
        _hasMoreNewer = false;
        Debug.WriteLine($"[MessageManager] Загружено {cached.Messages.Count} из кэша, hasMoreOlder={_hasMoreOlder}");

        if (syncState != null && (DateTime.UtcNow - syncState.LastSyncAt).TotalSeconds > CacheRevalidationThresholdSeconds)
        {
            Debug.WriteLine("[MessageManager] Кэш устарел, ревалидация");
            RunInBackground(() => RevalidateNewestAsync(_disposeCts.Token));
        }

        return LastIndexOrNull();
    }

    private async Task<int?> LoadInitialFromServerAsync(CancellationToken ct)
    {
        var url = ApiEndpoints.Messages.Latest(_chatId, DefaultPage);
        Debug.WriteLine($"[MessageManager] LoadInitialFromServer: {url}");

        var data = await FetchAsync(url, ct);
        if (data == null) return null;

        RenderMessages(data.Messages);
        SetBounds(data.HasMoreMessages, false);

        if (!ct.IsCancellationRequested)
            await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, false);

        return LastIndexOrNull();
    }

    private async Task<int?> LoadAroundCoreAsync(int messageId, CancellationToken ct)
    {
        if (_cacheService != null)
        {
            var cached = await Task.Run(() => _cacheService.GetMessagesAroundAsync(_chatId, messageId, DefaultPage), ct);
            if (cached is { IsComplete: true, Messages.Count: > 0 })
            {
                RenderMessages(cached.Messages);
                SetBounds(cached.HasMoreOlder, cached.HasMoreNewer);
                return FindIndexById(messageId);
            }
        }

        var data = await FetchAsync(ApiEndpoints.Messages.Around(_chatId, messageId, _userId, DefaultPage), ct);
        if (data == null) return null;

        RenderMessages(data.Messages);
        SetBounds(data.HasMoreMessages, data.HasNewerMessages);
        await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, data.HasNewerMessages);
        return FindIndexById(messageId);
    }

    private async Task LoadPageAsync(LoadDirection direction, CancellationToken ct)
    {
        Debug.WriteLine($"[LoadPage] {direction} вызван из:\n{Environment.StackTrace}");
        if (!GetHasMore(direction) || GetAnchor(direction) is not { } anchorId) return;

        var page = await LoadDirectionalAsync(direction, anchorId, LoadMorePage, ct);
        if (page == null || page.Messages.Count == 0)
        {
            SetHasMore(direction, false);
            Debug.WriteLine($"[LoadPage] {direction}: пустой ответ");
            return;
        }

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
        if (_cacheService != null)
        {
            var fromCache = await TryLoadDirectionalFromCacheAsync(dir, anchorId, count, ct);
            if (fromCache != null) return fromCache;
        }
        return await LoadDirectionalFromServerAsync(dir, anchorId, count, ct);
    }

    private async Task<DirectionalPage?> TryLoadDirectionalFromCacheAsync(LoadDirection dir, int anchorId, int count, CancellationToken ct)
    {
        var cached = dir == LoadDirection.Older
            ? await Task.Run(() => _cacheService!.GetMessagesBeforeAsync(_chatId, anchorId, count))
            : await Task.Run(() => _cacheService!.GetMessagesAfterAsync(_chatId, anchorId, count));

        if (cached is { IsComplete: true, Messages.Count: > 0 })
            return new(cached.Messages, GetCachedHasMore(cached, dir));

        if (dir == LoadDirection.Older && cached?.Messages is { Count: > 0 } partial)
        {
            var serverAnchor = partial.Min(m => m.Id);
            var data = await FetchAsync(ApiEndpoints.Messages.Before(_chatId, serverAnchor, _userId, count - partial.Count), ct);
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
                Debug.WriteLine($"[MessageManager] Лимит GapFill ({batches} батчей, +{totalAdded}) — сброс");
                await ResetToLatestAsync(ct);
            }
            else
            {
                Debug.WriteLine($"[MessageManager] GapFill: +{totalAdded} за {batches} батчей");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[MessageManager] Ошибка GapFill: {ex.Message}"); }
        finally { EndLoading(); }
    }

    private async Task<(int batches, int totalAdded)> GapFillLoopAsync(CancellationToken ct)
    {
        int batches = 0, totalAdded = 0;

        while (batches < MaxGapFillBatches)
        {
            ct.ThrowIfCancellationRequested();

            var data = await FetchAsync(ApiEndpoints.Messages.After(_chatId, _newestLoadedMessageId!.Value, _userId, DefaultPage), ct);
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
            if (!data.HasNewerMessages || data.Messages.Count < DefaultPage) break;
        }

        return (batches, totalAdded);
    }

    public async Task ResetToLatestAsync(CancellationToken ct)
    {
        try
        {
            if (_cacheService != null) await _cacheService.ClearChatMessagesAsync(_chatId);
            await Dispatcher.UIThread.InvokeAsync(ClearAllState);

            var data = await FetchAsync(ApiEndpoints.Messages.Latest(_chatId, DefaultPage), ct);
            if (data == null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RenderMessages(data.Messages);
                SetBounds(data.HasMoreMessages, false);
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
            var data = await FetchAsync(ApiEndpoints.Messages.After(_chatId, _newestLoadedMessageId.Value, _userId, DefaultPage), ct);
            if (data is not { Messages.Count: > 0 }) return;

            await SafeCacheMessagesAsync(data.Messages);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppendNewMessages(data.Messages);
                _hasMoreNewer = data.HasNewerMessages;
            });
            await SafeUpdateSyncStateAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[MessageManager] Ошибка ревалидации: {ex.Message}"); }
    }

    public void AddReceivedMessage(MessageDto message)
    {
        if (_disposeCts.IsCancellationRequested || !_loadedMessageIds.Add(message.Id)) return;

        var vm = CreateMessageViewModel(message);
        vm.IsNewIncoming = true;
        if (message.SenderId != _userId) vm.IsUnread = true;

        _messageIndex[message.Id] = vm;
        Messages.Add(vm);
        TrackBounds(message.Id);
        UpdateDateSeparatorForNewMessage(vm);
        MessageViewModel.UpdateGroupingAround(Messages, Messages.Count - 1);
        TrimOldMessagesFromStart();
        _hasMoreNewer = false;

        RunInBackground(async () =>
        {
            if (_cacheService == null) return;
            await SafeCacheAsync(() => _cacheService.UpsertMessageAsync(message));
            await SafeUpdateSyncStateAsync();
        });
    }

    public void HandleMessageDeleted(int messageId)
    {
        if (_disposeCts.IsCancellationRequested) return;

        var msg = FindMessage(messageId);
        if (msg != null)
        {
            msg.MarkAsDeleted();
            MessageViewModel.UpdateGroupingAround(Messages, Messages.IndexOf(msg));
        }

        foreach (var reply in Messages.Where(m => m.ReplyToMessageId == messageId))
        {
            reply.ReplyToIsDeleted = true;
            reply.ReplyToContent = null;
        }

        RunInBackground(() => SafeCacheIfAvailable(() => _cacheService!.MarkMessageDeletedAsync(messageId)));
    }

    public void HandleMessageUpdated(MessageDto dto)
    {
        if (_disposeCts.IsCancellationRequested) return;
        FindMessage(dto.Id)?.ApplyUpdate(dto);
    }

    public void HandlePollUpdated(PollDto pollDto)
    {
        if (_disposeCts.IsCancellationRequested || !HasLoadedPoll(pollDto.Id)) return;

        RunInBackground(async () =>
        {
            var currentUserPoll = await FetchPollForCurrentUserAsync(pollDto.Id, _disposeCts.Token);
            if (currentUserPoll == null) return;

            await Dispatcher.UIThread.InvokeAsync(() => ApplyPollUpdate(currentUserPoll));
        });
    }

    private void ApplyPollUpdate(PollDto pollDto)
    {
        if (_disposeCts.IsCancellationRequested) return;

        foreach (var msg in Messages.Where(m => m.PollDto?.Id == pollDto.Id || m.Poll?.PollId == pollDto.Id))
            msg.UpdatePoll(pollDto);
    }

    private bool HasLoadedPoll(int pollId)
        => Messages.Any(m => m.PollDto?.Id == pollId || m.Poll?.PollId == pollId);

    private async Task<PollDto?> FetchPollForCurrentUserAsync(int pollId, CancellationToken ct)
    {
        var result = await _apiClient.GetAsync<PollDto>(ApiEndpoints.Polls.ById(pollId, _userId), ct);

        return result is { Success: true, Data: not null } ? result.Data : null;
    }

    public void MarkAsReadLocally(int messageId)
    {
        foreach (var msg in Messages.Where(m => m.Id <= messageId && m.IsUnread))
            msg.IsUnread = false;

        if (!LastReadMessageId.HasValue || messageId > LastReadMessageId.Value)
            LastReadMessageId = messageId;
    }

    public IEnumerable<MessageViewModel> GetUnreadMessages()
        => Messages.Where(m => m.IsUnread && m.SenderId != _userId);

    private void PrependNewMessages(List<MessageDto> dtos) => MutateMessages(dtos, append: false);
    private void AppendNewMessages(List<MessageDto> dtos) => MutateMessages(dtos, append: true);

    private void MutateMessages(List<MessageDto> dtos, bool append)
    {
        var lookup = GetMembersLookup();
        var newVms = new List<MessageViewModel>(dtos.Count);

        foreach (var msg in dtos.OrderBy(m => m.Id))
        {
            if (!_loadedMessageIds.Add(msg.Id)) continue;
            var vm = CreateMessageViewModel(msg, lookup);
            newVms.Add(vm);
            _messageIndex[msg.Id] = vm;
            TrackBounds(msg.Id);
        }

        if (newVms.Count == 0) return;

        if (append)
        {
            var startIndex = Messages.Count;
            Messages.AddRange(newVms);
            UpdateDateSeparatorsForRange(startIndex > 0 ? startIndex - 1 : 0, Messages.Count - 1);
            UpdateGroupingFrom(Math.Max(0, startIndex - 1));
            TrimOldMessagesFromStart();
        }
        else
        {
            PrecomputeGroupingForPrepend(newVms);
            Messages.InsertRange(0, newVms);
            FixBoundaryAfterPrepend(newVms.Count);
            UpdateDateSeparatorsForRange(0, Math.Min(newVms.Count, Messages.Count - 1));
            TrimNewMessagesFromEnd();
        }
    }

    private static void PrecomputeGroupingForPrepend(List<MessageViewModel> newVms)
    {
        for (int i = 0; i < newVms.Count; i++)
        {
            var prev = i > 0 ? newVms[i - 1] : null;
            var next = i < newVms.Count - 1 ? newVms[i + 1] : null;
            newVms[i].IsContinuation = prev != null && MessageViewModel.CanGroup(prev, newVms[i]);
            newVms[i].HasNextFromSame = next != null && MessageViewModel.CanGroup(newVms[i], next);
        }
    }

    private void FixBoundaryAfterPrepend(int insertedCount)
    {
        if (insertedCount >= Messages.Count) return;

        var lastNew = Messages[insertedCount - 1];
        var firstOld = Messages[insertedCount];
        bool shouldLink = MessageViewModel.CanGroup(lastNew, firstOld);

        lastNew.HasNextFromSame = shouldLink;
        firstOld.IsContinuation = shouldLink;
    }

    private void TrimOldMessagesFromStart()
    {
        if (Messages.Count <= MaxMessagesInMemory) return;
        TrimMessages(Messages.Take(Math.Min(TrimBatchSize, Messages.Count - MaxMessagesInMemory)), ref _hasMoreOlder);
    }

    private void TrimNewMessagesFromEnd()
    {
        if (Messages.Count <= MaxMessagesInMemory) return;
        TrimMessages(Messages.Skip(Messages.Count - Math.Min(TrimBatchSize, Messages.Count - MaxMessagesInMemory)), ref _hasMoreNewer);
    }

    private void TrimMessages(IEnumerable<MessageViewModel> toRemove, ref bool hasMoreFlag)
    {
        var list = toRemove.ToList();
        foreach (var msg in list)
        {
            _loadedMessageIds.Remove(msg.Id);
            _messageIndex.Remove(msg.Id);
            DisposeMessage(msg);
        }
        Messages.RemoveRange(list);
        hasMoreFlag = true;
        RecalculateBounds();
    }

    private void RenderMessages(List<MessageDto> messages)
    {
        ClearAllState();

        var lookup = GetMembersLookup();
        var newVms = new List<MessageViewModel>();

        foreach (var msg in messages.OrderBy(m => m.Id))
        {
            if (!_loadedMessageIds.Add(msg.Id)) continue;
            var vm = CreateMessageViewModel(msg, lookup);
            newVms.Add(vm);
            _messageIndex[msg.Id] = vm;
            TrackBounds(msg.Id);
        }

        Messages.AddRange(newVms);
        UpdateDateSeparators();
        RecalculateGrouping();
    }

    private void ClearAllState()
    {
        DisposeAllMessages();
        Messages.Clear();
        _loadedMessageIds.Clear();
        _messageIndex.Clear();
        _oldestLoadedMessageId = _newestLoadedMessageId = null;
        _hasMoreOlder = true;
        _hasMoreNewer = false;
    }

    private int AppendAndCount(List<MessageDto> dtos)
    {
        var before = Messages.Count;
        AppendNewMessages(dtos);
        return Messages.Count - before;
    }

    private MessageViewModel CreateMessageViewModel(MessageDto msg, Dictionary<int, UserDto>? lookup = null)
    {
        lookup ??= GetMembersLookup();
        lookup.TryGetValue(msg.SenderId ?? 0, out var sender);
        msg.IsOwn = msg.SenderId == _userId;

        return new MessageViewModel(msg, _downloadService, _notificationService, _audioPlayer, _apiClient, _userId, _stateService)
        {
            SenderName = sender?.DisplayName ?? sender?.Username ?? msg.SenderName ?? "Unknown",
            SenderAvatar = sender?.Avatar ?? msg.SenderAvatarUrl,
            MentionClickCommand = mentionClickCommand,
            Commands = chatCommands,
            IsUnread = LastReadMessageId.HasValue && msg.Id > LastReadMessageId.Value && msg.SenderId != _userId
        };
    }

    private Dictionary<int, UserDto> GetMembersLookup()
        => _membersLookup ??= _getMembersFunc().ToDictionary(m => m.Id);

    private enum LoadDirection { Older, Newer }
    private sealed record DirectionalPage(List<MessageDto> Messages, bool HasMore);

    private int? GetAnchor(LoadDirection d) => d == LoadDirection.Older ? _oldestLoadedMessageId : _newestLoadedMessageId;
    private bool GetHasMore(LoadDirection d) => d == LoadDirection.Older ? _hasMoreOlder : _hasMoreNewer;
    private void SetHasMore(LoadDirection d, bool value) { if (d == LoadDirection.Older) _hasMoreOlder = value; else _hasMoreNewer = value; }

    private string BuildDirectionalUrl(LoadDirection d, int anchor, int count) => d == LoadDirection.Older
        ? ApiEndpoints.Messages.Before(_chatId, anchor, _userId, count)
        : ApiEndpoints.Messages.After(_chatId, anchor, _userId, count);

    private static bool GetCachedHasMore(CachedMessagesResult c, LoadDirection d) => d == LoadDirection.Older ? c.HasMoreOlder : c.HasMoreNewer;
    private static bool GetServerHasMore(PagedMessagesDto p, LoadDirection d) => d == LoadDirection.Older ? p.HasMoreMessages : p.HasNewerMessages;

    private void SetBounds(bool hasOlder, bool hasNewer) { _hasMoreOlder = hasOlder; _hasMoreNewer = hasNewer; }

    private void TrackBounds(int id)
    {
        _oldestLoadedMessageId = _oldestLoadedMessageId.HasValue ? Math.Min(_oldestLoadedMessageId.Value, id) : id;
        _newestLoadedMessageId = _newestLoadedMessageId.HasValue ? Math.Max(_newestLoadedMessageId.Value, id) : id;
    }

    private void RecalculateBounds()
    {
        if (Messages.Count == 0) { _oldestLoadedMessageId = _newestLoadedMessageId = null; return; }
        _oldestLoadedMessageId = Messages[0].Id;
        _newestLoadedMessageId = Messages[^1].Id;
    }

    private MessageViewModel? FindMessage(int id) => _messageIndex.TryGetValue(id, out var vm) ? vm : null;
    private int? LastIndexOrNull() => Messages.Count > 0 ? Messages.Count - 1 : null;

    private int? FindIndexById(int id)
    {
        for (var i = 0; i < Messages.Count; i++)
            if (Messages[i].Id == id) return i;
        return null;
    }

    private static List<MessageDto> MergeAndDeduplicate(List<MessageDto>? first, List<MessageDto> second)
        => [.. (first ?? []).Concat(second).GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.Id)];

    private void RecalculateGrouping() => MessageViewModel.RecalculateGrouping(Messages);

    private void UpdateGroupingFrom(int startIndex)
    {
        for (var i = startIndex; i < Messages.Count; i++)
        {
            var prev = i > 0 ? Messages[i - 1] : null;
            var next = i < Messages.Count - 1 ? Messages[i + 1] : null;
            Messages[i].IsContinuation = prev != null && MessageViewModel.CanGroup(prev, Messages[i]);
            Messages[i].HasNextFromSame = next != null && MessageViewModel.CanGroup(Messages[i], next);
        }
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

    private void UpdateDateSeparatorsForRange(int startIndex, int endIndex)
    {
        for (var i = startIndex; i <= endIndex; i++)
        {
            var date = Messages[i].CreatedAt.Date;
            var prevDate = i > 0 ? Messages[i - 1].CreatedAt.Date : (DateTime?)null;
            var isNew = prevDate == null || date != prevDate.Value;
            Messages[i].ShowDateSeparator = isNew;
            Messages[i].DateSeparatorText = isNew ? FormatDateSeparator(date) : null;
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

    private bool TryBeginLoading() => Interlocked.CompareExchange(ref _isLoading, 1, 0) == 0;
    private void EndLoading() => Interlocked.Exchange(ref _isLoading, 0);

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
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[MessageManager] Фоновая ошибка в {caller}: {ex.Message}"); }
        }, token);

        lock (_bgLock)
        {
            _backgroundTasks.RemoveAll(t => t.IsCompleted);
            _backgroundTasks.Add(task);
        }
    }

    private async Task<PagedMessagesDto?> FetchAsync(string url, CancellationToken ct)
    {
        Debug.WriteLine($"[FetchAsync] {url}");
        if (ct.IsCancellationRequested) return null;

        var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

        if (result?.Data?.Messages != null)
        {
            foreach (var msg in result.Data.Messages.Where(m => m.Poll != null))
                Debug.WriteLine($"[FetchAsync] Poll id={msg.Id}: pollId={msg.Poll!.Id} options={msg.Poll.Options?.Count ?? -1}");
        }

        Debug.WriteLine($"[MessageManager] FetchAsync: success={result?.Success}, error={result?.Error}, count={result?.Data?.Messages?.Count ?? -1}");
        return result is { Success: true, Data: not null } ? result.Data : null;
    }

    private static async Task SafeCacheAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Debug.WriteLine($"[MessageManager] Ошибка кеша: {ex.Message}"); }
    }

    private Task SafeCacheIfAvailable(Func<Task> action)
        => _cacheService != null ? SafeCacheAsync(action) : Task.CompletedTask;

    private Task SafeCacheMessagesAsync(List<MessageDto> msgs)
        => msgs.Count > 0 ? SafeCacheIfAvailable(() => _cacheService!.UpsertMessagesAsync(msgs)) : Task.CompletedTask;

    private Task SafeUpdateSyncStateAsync()
        => SafeCacheIfAvailable(() => _cacheService!.UpdateSyncStateAsync(BuildSyncState()));

    private async Task SafeSaveToCacheAsync(List<MessageDto> msgs, bool hasOlder, bool hasNewer)
    {
        if (_cacheService == null || msgs.Count == 0) return;
        await SafeCacheAsync(async () =>
        {
            await _cacheService.UpsertMessagesAsync(msgs);
            await _cacheService.UpdateSyncStateAsync(BuildSyncState(hasOlder, hasNewer));
        });
    }

    private ChatSyncState BuildSyncState(bool? hasOlder = null, bool? hasNewer = null) => new()
    {
        ChatId = _chatId,
        OldestLoadedId = _oldestLoadedMessageId,
        NewestLoadedId = _newestLoadedMessageId,
        HasMoreOlder = hasOlder ?? _hasMoreOlder,
        HasMoreNewer = hasNewer ?? _hasMoreNewer
    };

    private void DisposeAllMessages()
    {
        foreach (var msg in Messages)
        {
            foreach (var file in msg.FileViewModels)
                (file as IDisposable)?.Dispose();
            msg.Dispose();
        }
    }

    private static void DisposeMessage(MessageViewModel msg)
    {
        foreach (var file in msg.FileViewModels)
            (file as IDisposable)?.Dispose();
        msg.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();

        Task[] pending;
        lock (_bgLock) pending = [.. _backgroundTasks];

        try { await Task.WhenAll(pending); }
        catch { }

        DisposeAllMessages();
        Messages.Clear();
        _messageIndex.Clear();
        _loadedMessageIds.Clear();
        _disposeCts.Dispose();
    }
}