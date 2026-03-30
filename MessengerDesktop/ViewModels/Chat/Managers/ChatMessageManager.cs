using MessengerDesktop.Data.Entities;
using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public sealed class ChatMessageManager(int chatId, int userId, IApiClientService apiClient,
    Func<ObservableCollection<UserDto>> getMembersFunc, IFileDownloadService? downloadService = null,
    INotificationService? notificationService = null, ILocalCacheService? cacheService = null) : IAsyncDisposable
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly Func<ObservableCollection<UserDto>> _getMembersFunc = getMembersFunc ?? throw new ArgumentNullException(nameof(getMembersFunc));

    private int? _oldestLoadedMessageId;
    private int? _newestLoadedMessageId;
    private bool _hasMoreOlder = true;
    private bool _hasMoreNewer;
    private readonly HashSet<int> _loadedMessageIds = [];

    private int _isLoading;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Lock _bgLock = new();
    private readonly List<Task> _backgroundTasks = [];

    private const int MaxGapFillBatches = 5;

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

    #region Загрузка сообщений

    public async Task<int?> LoadInitialMessagesAsync(CancellationToken ct = default)
    {
        if (!TryBeginLoading()) return null;

        try
        {
            if (FirstUnreadMessageId.HasValue)
                return await LoadMessagesAroundInternalAsync(FirstUnreadMessageId.Value, ct);

            if (cacheService != null)
            {
                var cached = await cacheService.GetMessagesAsync(chatId, AppConstants.DefaultPageSize);

                if (cached is { Messages.Count: > 0 })
                {
                    RenderMessages(cached.Messages);
                    _hasMoreOlder = cached.HasMoreOlder;
                    _hasMoreNewer = false;

                    Debug.WriteLine($"[MessageManager] Загружено {cached.Messages.Count} из кеша для чата {chatId}");

                    RunInBackground(() => RevalidateNewestAsync(_disposeCts.Token));
                    return Messages.Count > 0 ? Messages.Count - 1 : null;
                }
            }

            return await LoadInitialFromServerAsync(ct);
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task<int?> LoadInitialFromServerAsync(CancellationToken ct)
    {
        var url = ApiEndpoints.Messages.ForChat(chatId, userId, 1, AppConstants.DefaultPageSize);
        var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

        if (result is not { Success: true, Data: not null })
            return null;

        RenderMessages(result.Data.Messages);
        _hasMoreOlder = result.Data.HasMoreMessages;
        _hasMoreNewer = false;

        await SaveToCacheAsync(result.Data.Messages, result.Data.HasMoreMessages, false);

        return Messages.Count > 0 ? Messages.Count - 1 : null;
    }

    public async Task<int?> LoadMessagesAroundAsync(int messageId, CancellationToken ct = default)
    {
        if (!TryBeginLoading()) return null;

        try
        {
            return await LoadMessagesAroundInternalAsync(messageId, ct);
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task<int?> LoadMessagesAroundInternalAsync(int messageId, CancellationToken ct = default)
    {
        if (cacheService != null)
        {
            var cached = await cacheService.GetMessagesAroundAsync(chatId, messageId, AppConstants.DefaultPageSize);

            if (cached is { IsComplete: true, Messages.Count: > 0 })
            {
                RenderMessages(cached.Messages);
                _hasMoreOlder = cached.HasMoreOlder;
                _hasMoreNewer = cached.HasMoreNewer;
                return FindIndexById(Messages, messageId);
            }
        }

        var url = ApiEndpoints.Messages.Around(chatId, messageId, userId, AppConstants.DefaultPageSize);
        var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

        if (result is not { Success: true, Data: not null })
            return null;

        RenderMessages(result.Data.Messages);
        _hasMoreOlder = result.Data.HasMoreMessages;
        _hasMoreNewer = result.Data.HasNewerMessages;

        await SaveToCacheAsync(result.Data.Messages, result.Data.HasMoreMessages, result.Data.HasNewerMessages);

        return FindIndexById(Messages, messageId);
    }

    private enum LoadDirection { Older, Newer }

    public Task LoadOlderMessagesAsync(CancellationToken ct = default)
        => LoadPageAsync(LoadDirection.Older, ct);

    public Task LoadNewerMessagesAsync(CancellationToken ct = default)
        => LoadPageAsync(LoadDirection.Newer, ct);

    private async Task LoadPageAsync(LoadDirection direction, CancellationToken ct)
    {
        var anchor = direction == LoadDirection.Older ? _oldestLoadedMessageId : _newestLoadedMessageId;
        var hasMore = direction == LoadDirection.Older ? _hasMoreOlder : _hasMoreNewer;

        if (!TryBeginLoading()) return;

        try
        {
            if (!hasMore || anchor is not { } anchorId) return;

            var page = await LoadDirectionalAsync(direction, anchorId, AppConstants.LoadMorePageSize, ct);
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

            await UpdateSyncStateAsync();
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task<DirectionalPage?> LoadDirectionalAsync(LoadDirection direction, int anchorId, int count, CancellationToken ct)
    {
        if (cacheService != null)
        {
            var cached = direction == LoadDirection.Older
                ? await cacheService.GetMessagesBeforeAsync(chatId, anchorId, count)
                : await cacheService.GetMessagesAfterAsync(chatId, anchorId, count);

            if (cached is { IsComplete: true, Messages.Count: > 0 })
            {
                var cachedHasMore = direction == LoadDirection.Older
                    ? cached.HasMoreOlder : cached.HasMoreNewer;
                return new(cached.Messages, cachedHasMore);
            }

            if (direction == LoadDirection.Older && cached?.Messages is { Count: > 0 } partial)
            {
                var serverAnchor = partial.Min(m => m.Id);
                var serverCount = count - partial.Count;

                var url = ApiEndpoints.Messages.Before(chatId, serverAnchor, userId, serverCount);
                var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

                if (result is not { Success: true, Data: not null })
                    return null;

                await CacheMessagesOnlyAsync(result.Data.Messages);
                return new(MergeAndDeduplicate(partial, result.Data.Messages), result.Data.HasMoreMessages);
            }
        }
        {
            var url = direction == LoadDirection.Older
                ? ApiEndpoints.Messages.Before(chatId, anchorId, userId, count)
                : ApiEndpoints.Messages.After(chatId, anchorId, userId, count);

            var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);
            if (result is not { Success: true, Data: not null })
                return null;

            await CacheMessagesOnlyAsync(result.Data.Messages);

            var serverHasMore = direction == LoadDirection.Older
                ? result.Data.HasMoreMessages : result.Data.HasNewerMessages;

            return new(result.Data.Messages, serverHasMore);
        }
    }

    private record DirectionalPage(List<MessageDto> Messages, bool HasMore);

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

                var url = ApiEndpoints.Messages.After(chatId, _newestLoadedMessageId!.Value, userId, AppConstants.DefaultPageSize);
                var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

                if (result is not { Success: true, Data.Messages.Count: > 0 })
                    break;

                batchesLoaded++;
                await CacheMessagesOnlyAsync(result.Data.Messages);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    totalAdded += AppendNewMessages(result.Data.Messages);
                    _hasMoreNewer = result.Data.HasNewerMessages;
                });

                await UpdateSyncStateAsync();

                if (!result.Data.HasNewerMessages || result.Data.Messages.Count < AppConstants.DefaultPageSize)
                    break;
            }

            if (batchesLoaded >= MaxGapFillBatches)
            {
                Debug.WriteLine($"[MessageManager] Лимит GapFill ({MaxGapFillBatches} батчей, {totalAdded} сообщений). Сброс.");
                await ResetToLatestAsync(ct);
            }
            else
            {
                Debug.WriteLine($"[MessageManager] GapFill завершён: {totalAdded} сообщений за {batchesLoaded} батчей");
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка GapFill: {ex.Message}");
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task ResetToLatestAsync(CancellationToken ct)
    {
        try
        {
            if (cacheService != null)
                await cacheService.ClearChatMessagesAsync(chatId);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DisposeAllMessages();
                Messages.Clear();
                _loadedMessageIds.Clear();
                _oldestLoadedMessageId = null;
                _newestLoadedMessageId = null;
                _hasMoreOlder = true;
                _hasMoreNewer = false;
            });

            var url = ApiEndpoints.Messages.ForChat(chatId, userId, 1, AppConstants.DefaultPageSize);
            var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

            if (result is { Success: true, Data: not null })
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RenderMessages(result.Data.Messages);
                    _hasMoreOlder = result.Data.HasMoreMessages;
                    _hasMoreNewer = false;
                });

                await SaveToCacheAsync(result.Data.Messages, result.Data.HasMoreMessages, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка сброса: {ex.Message}");
        }
    }

    private async Task RevalidateNewestAsync(CancellationToken ct)
    {
        if (_newestLoadedMessageId == null) return;

        try
        {
            var url = ApiEndpoints.Messages.After(chatId, _newestLoadedMessageId.Value, userId, AppConstants.DefaultPageSize);
            var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

            if (result is not { Success: true, Data.Messages.Count: > 0 })
                return;

            await CacheMessagesOnlyAsync(result.Data.Messages);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppendNewMessages(result.Data.Messages);
                _hasMoreNewer = result.Data.HasNewerMessages;
            });

            await UpdateSyncStateAsync();
        }
        catch (OperationCanceledException) { /* Закрытие чата */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка ревалидации: {ex.Message}");
        }
    }

    #endregion

    #region Обработка событий реального времени

    public void AddReceivedMessage(MessageDto message)
    {
        if (!_loadedMessageIds.Add(message.Id))
            return;

        var vm = CreateMessageViewModel(message);

        if (message.SenderId != userId)
            vm.IsUnread = true;

        Messages.Add(vm);
        TrackBounds(message.Id);
        UpdateDateSeparatorForNewMessage(vm);
        MessageViewModel.UpdateGroupingAround(Messages, Messages.Count - 1);

        _hasMoreNewer = false;

        RunInBackground(async () =>
        {
            if (cacheService == null) return;
            await cacheService.UpsertMessageAsync(message);
            await UpdateSyncStateAsync();
        });
    }

    public void HandleMessageDeleted(int messageId)
    {
        var msg = Messages.FirstOrDefault(m => m.Id == messageId);
        if (msg == null) return;

        var index = Messages.IndexOf(msg);
        msg.MarkAsDeleted();
        MessageViewModel.UpdateGroupingAround(Messages, index);

        RunInBackground(async () =>
        {
            if (cacheService != null)
                await cacheService.MarkMessageDeletedAsync(messageId);
        });
    }

    public void HandleMessageUpdated(MessageDto updatedDto)
    {
        var msg = Messages.FirstOrDefault(m => m.Id == updatedDto.Id);
        if (msg == null) return;

        msg.ApplyUpdate(updatedDto);

        RunInBackground(async () =>
        {
            if (cacheService != null)
                await cacheService.UpsertMessageAsync(updatedDto);
        });
    }

    #endregion

    #region Статус прочтения

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

    #region Работа с UI-коллекцией (вызывать на UI-потоке)

    private int AppendNewMessages(List<MessageDto> dtos)
    {
        var lookup = BuildMembersLookup();
        var startIndex = Messages.Count;
        var added = 0;

        foreach (var msg in dtos)
        {
            if (!_loadedMessageIds.Add(msg.Id)) continue;
            Messages.Add(CreateMessageViewModel(msg, lookup));
            TrackBounds(msg.Id);
            added++;
        }

        if (added > 0)
        {
            UpdateDateSeparators();
            UpdateGroupingFrom(Math.Max(0, startIndex - 1));
        }

        return added;
    }

    private int PrependNewMessages(List<MessageDto> dtos)
    {
        var lookup = BuildMembersLookup();
        var added = 0;

        for (int i = dtos.Count - 1; i >= 0; i--)
        {
            var msg = dtos[i];
            if (!_loadedMessageIds.Add(msg.Id)) continue;
            Messages.Insert(0, CreateMessageViewModel(msg, lookup));
            TrackBounds(msg.Id);
            added++;
        }

        if (added > 0)
        {
            UpdateDateSeparators();
            RecalculateGrouping();
        }

        return added;
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
            foreach (var file in msg.FileViewModels)
            {
                if (file is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private MessageViewModel CreateMessageViewModel(MessageDto msg, Dictionary<int, UserDto>? lookup = null)
    {
        lookup ??= BuildMembersLookup();
        lookup.TryGetValue(msg.SenderId, out var sender);

        var vm = new MessageViewModel(msg, downloadService, notificationService)
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

    #region Кеширование

    private async Task SaveToCacheAsync(List<MessageDto> messages, bool hasMoreOlder, bool hasMoreNewer)
    {
        if (cacheService == null || messages.Count == 0) return;

        try
        {
            await cacheService.UpsertMessagesAsync(messages);
            await cacheService.UpdateSyncStateAsync(BuildSyncState(hasMoreOlder, hasMoreNewer));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка сохранения в кеш: {ex.Message}");
        }
    }

    private async Task CacheMessagesOnlyAsync(List<MessageDto> messages)
    {
        if (cacheService == null || messages.Count == 0) return;

        try
        {
            await cacheService.UpsertMessagesAsync(messages);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка записи в кеш: {ex.Message}");
        }
    }

    private async Task UpdateSyncStateAsync()
    {
        if (cacheService == null) return;

        try
        {
            await cacheService.UpdateSyncStateAsync(BuildSyncState(_hasMoreOlder, _hasMoreNewer));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Ошибка обновления SyncState: {ex.Message}");
        }
    }

    private ChatSyncState BuildSyncState(bool hasMoreOlder, bool hasMoreNewer) => new()
    {
        ChatId = chatId,
        OldestLoadedId = _oldestLoadedMessageId,
        NewestLoadedId = _newestLoadedMessageId,
        HasMoreOlder = hasMoreOlder,
        HasMoreNewer = hasMoreNewer
    };

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

    #region Вспомогательные методы

    private static List<MessageDto> MergeAndDeduplicate(List<MessageDto>? first, List<MessageDto> second)
        => [.. (first ?? []).Concat(second).GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.Id)];

    private static int? FindIndexById(ObservableCollection<MessageViewModel> messages, int id)
    {
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].Id == id)
                return i;
        }
        return null;
    }

    #endregion

    #region Группировка

    private void RecalculateGrouping()
        => MessageViewModel.RecalculateGrouping(Messages);

    private void UpdateGroupingFrom(int startIndex)
    {
        for (int i = startIndex; i < Messages.Count; i++)
        {
            var current = Messages[i];
            var prev = i > 0 ? Messages[i - 1] : null;
            var next = i < Messages.Count - 1 ? Messages[i + 1] : null;

            current.IsContinuation = prev != null && MessageViewModel.CanGroup(prev, current);
            current.HasNextFromSame = next != null && MessageViewModel.CanGroup(current, next);
        }
    }

    #endregion

    #region Границы и разделители дат

    private void TrackBounds(int id)
    {
        _oldestLoadedMessageId = _oldestLoadedMessageId.HasValue ? Math.Min(_oldestLoadedMessageId.Value, id) : id;
        _newestLoadedMessageId = _newestLoadedMessageId.HasValue ? Math.Max(_newestLoadedMessageId.Value, id) : id;
    }

    private void UpdateDateSeparators()
    {
        DateTime? previousDate = null;

        foreach (var message in Messages)
        {
            var messageDate = message.CreatedAt.Date;

            if (previousDate == null || messageDate != previousDate.Value)
            {
                message.ShowDateSeparator = true;
                message.DateSeparatorText = FormatDateSeparator(messageDate);
            }
            else
            {
                message.ShowDateSeparator = false;
                message.DateSeparatorText = null;
            }

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

    #region Dispose

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();

        Task[] pending;
        lock (_bgLock) pending = [.. _backgroundTasks];

        try { await Task.WhenAll(pending); }
        catch { /* expected */ }

        DisposeAllMessages();
        _disposeCts.Dispose();
    }
    #endregion
}