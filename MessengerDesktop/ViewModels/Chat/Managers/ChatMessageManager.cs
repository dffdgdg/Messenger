using MessengerDesktop.Data.Entities;
using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public class ChatMessageManager(int chatId,int userId,
    IApiClientService apiClient, Func<ObservableCollection<UserDto>> getMembersFunc,
    IFileDownloadService? downloadService = null,
    INotificationService? notificationService = null,
    ILocalCacheService? cacheService = null)
{
    private readonly IApiClientService _apiClient = apiClient
            ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly Func<ObservableCollection<UserDto>> _getMembersFunc = getMembersFunc
            ?? throw new ArgumentNullException(nameof(getMembersFunc));
    private int? _oldestLoadedMessageId;
    private int? _newestLoadedMessageId;
    private bool _hasMoreOlder = true;
    private bool _hasMoreNewer;
    private readonly HashSet<int> _loadedMessageIds = [];

    private const int MaxGapFillBatches = 5;

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public bool IsLoading { get; private set; }
    public bool HasMoreOlder => _hasMoreOlder;
    public bool HasMoreNewer => _hasMoreNewer;
    public int? LastReadMessageId { get; private set; }
    public int? FirstUnreadMessageId { get; private set; }

    public void SetReadInfo(ChatReadInfoDto? info)
    {
        if (info == null) return;

        LastReadMessageId = info.LastReadMessageId;
        FirstUnreadMessageId = info.FirstUnreadMessageId;

        Debug.WriteLine("[MessageManager] ReadInfo: lastRead={LastReadMessageId}, " +
            $"firstUnread={FirstUnreadMessageId}");
    }

    public async Task<int?> LoadInitialMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading) return null;
        IsLoading = true;

        try
        {
            if (FirstUnreadMessageId.HasValue)
            {
                return await LoadMessagesAroundInternalAsync(FirstUnreadMessageId.Value, ct);
            }

            if (cacheService != null)
            {
                var cached = await cacheService.GetMessagesAsync(
                    chatId, AppConstants.DefaultPageSize);

                if (cached is { Messages.Count: > 0 })
                {
                    RenderMessages(cached.Messages);
                    _hasMoreOlder = cached.HasMoreOlder;
                    _hasMoreNewer = false;

                    var scrollIndex = Messages.Count > 0
                        ? Messages.Count - 1
                        : (int?)null;

                    Debug.WriteLine($"[MessageManager] Loaded {cached.Messages.Count} messages " +
                        $"from CACHE for chat {chatId}");

                    _ = RevalidateNewestAsync(ct);

                    return scrollIndex;
                }
            }

            return await LoadInitialFromServerAsync(ct);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<int?> LoadInitialFromServerAsync(CancellationToken ct)
    {
        var url = ApiEndpoints.Messages.ForChat(chatId, userId, 1, AppConstants.DefaultPageSize);
        var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

        if (result is { Success: true, Data: not null })
        {
            RenderMessages(result.Data.Messages);
            _hasMoreOlder = result.Data.HasMoreMessages;
            _hasMoreNewer = false;

            await SaveToCacheAsync(result.Data.Messages, result.Data.HasMoreMessages, false);

            return Messages.Count > 0 ? Messages.Count - 1 : null;
        }

        return null;
    }

    public async Task<int?> LoadMessagesAroundAsync(int messageId, CancellationToken ct = default)
    {
        if (IsLoading) return null;
        IsLoading = true;

        try
        {
            return await LoadMessagesAroundInternalAsync(
                messageId, ct);
        }
        finally
        {
            IsLoading = false;
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

                var targetIndex = Messages.ToList().FindIndex(m => m.Id == messageId);
                return targetIndex >= 0 ? targetIndex : null;
            }
        }

        var url = ApiEndpoints.Messages.Around(chatId, messageId, userId, AppConstants.DefaultPageSize);
        var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

        if (result is { Success: true, Data: not null })
        {
            RenderMessages(result.Data.Messages);
            _hasMoreOlder = result.Data.HasMoreMessages;
            _hasMoreNewer = result.Data.HasNewerMessages;

            int? targetIndex = null;
            for (int i = 0; i < result.Data.Messages.Count; i++)
            {
                if (result.Data.Messages[i].Id == messageId)
                {
                    targetIndex = i;
                    break;
                }
            }

            await SaveToCacheAsync(
                result.Data.Messages,
                result.Data.HasMoreMessages,
                result.Data.HasNewerMessages);

            return targetIndex;
        }

        return null;
    }

    public async Task LoadOlderMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading || !_hasMoreOlder || !_oldestLoadedMessageId.HasValue)
            return;
        IsLoading = true;

        try
        {
            const int requestedCount = AppConstants.LoadMorePageSize;
            List<MessageDto>? messagesToPrepend = null;
            bool hasMore;

            if (cacheService != null)
            {
                var cached = await cacheService.GetMessagesBeforeAsync(
                    chatId, _oldestLoadedMessageId.Value,
                    requestedCount);

                if (cached is { IsComplete: true, Messages.Count: > 0 })
                {
                    messagesToPrepend = cached.Messages;
                    hasMore = cached.HasMoreOlder;
                }
                else
                {
                    var cacheCount = cached?.Messages.Count ?? 0;
                    var serverBeforeId = cacheCount > 0 ? cached!.Messages.Min(m => m.Id)
                        : _oldestLoadedMessageId.Value;

                    var url = ApiEndpoints.Messages.Before(chatId, serverBeforeId, userId,
                        requestedCount - cacheCount);
                    var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

                    if (result is { Success: true, Data: not null })
                    {
                        await CacheMessagesOnlyAsync(result.Data.Messages);

                        messagesToPrepend =
                        [.. (cached?.Messages ?? [])
                            .Concat(result.Data.Messages)
                            .GroupBy(m => m.Id)
                            .Select(g => g.First())
                            .OrderBy(m => m.Id)];

                        hasMore = result.Data.HasMoreMessages;
                    }
                    else
                    {
                        return;
                    }
                }
            }
            else
            {
                var url = ApiEndpoints.Messages.Before(chatId, _oldestLoadedMessageId.Value,
                    userId, requestedCount);
                var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

                if (result is not { Success: true, Data: not null })
                    return;

                messagesToPrepend = result.Data.Messages;
                hasMore = result.Data.HasMoreMessages;
            }

            if (messagesToPrepend is { Count: > 0 })
            {
                var members = _getMembersFunc();

                for (int i = messagesToPrepend.Count - 1; i >= 0; i--)
                {
                    var msg = messagesToPrepend[i];
                    if (!_loadedMessageIds.Add(msg.Id)) continue;
                    Messages.Insert(0, CreateMessageViewModel(msg, members));
                }

                UpdateBounds();
                UpdateDateSeparators();
                RecalculateGrouping();
                _hasMoreOlder = hasMore;

                await UpdateSyncStateAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadNewerMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading || !_hasMoreNewer || !_newestLoadedMessageId.HasValue)
            return;
        IsLoading = true;

        try
        {
            List<MessageDto>? messagesToAppend = null;
            bool hasNewer;

            if (cacheService != null)
            {
                var cached = await cacheService.GetMessagesAfterAsync(chatId, _newestLoadedMessageId.Value,
                    AppConstants.LoadMorePageSize);

                if (cached is
                    { IsComplete: true, Messages.Count: > 0 })
                {
                    messagesToAppend = cached.Messages;
                    hasNewer = cached.HasMoreNewer;
                }
                else
                {
                    var url = ApiEndpoints.Messages.After(chatId, _newestLoadedMessageId.Value,
                        userId, AppConstants.LoadMorePageSize);
                    var result =
                        await _apiClient
                            .GetAsync<PagedMessagesDto>(url, ct);

                    if (result is not { Success: true, Data: not null })
                        return;

                    await CacheMessagesOnlyAsync(result.Data.Messages);
                    messagesToAppend = result.Data.Messages;
                    hasNewer = result.Data.HasNewerMessages;
                }
            }
            else
            {
                var url = ApiEndpoints.Messages.After(chatId, _newestLoadedMessageId.Value,
                    userId, AppConstants.LoadMorePageSize);
                var result =
                    await _apiClient
                        .GetAsync<PagedMessagesDto>(url, ct);

                if (result is not { Success: true, Data: not null })
                    return;

                messagesToAppend = result.Data.Messages;
                hasNewer = result.Data.HasNewerMessages;
            }

            if (messagesToAppend is { Count: > 0 })
            {
                var members = _getMembersFunc();
                var startIndex = Messages.Count;

                foreach (var msg in messagesToAppend)
                {
                    if (!_loadedMessageIds.Add(msg.Id)) continue;
                    Messages.Add(
                        CreateMessageViewModel(msg, members));
                }

                UpdateBounds();
                UpdateDateSeparators();

                if (startIndex > 0)
                {
                    for (int i = Math.Max(0, startIndex - 1);
                         i < Messages.Count; i++)
                    {
                        UpdateGroupingForIndex(i);
                    }
                }
                else
                {
                    RecalculateGrouping();
                }

                _hasMoreNewer = hasNewer;
                await UpdateSyncStateAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task GapFillAfterReconnectAsync(CancellationToken ct = default)
    {
        if (_newestLoadedMessageId == null) return;

        var batchesLoaded = 0;
        var totalAdded = 0;

        try
        {
            while (batchesLoaded < MaxGapFillBatches)
            {
                ct.ThrowIfCancellationRequested();

                var afterId = _newestLoadedMessageId!.Value;
                var url = ApiEndpoints.Messages.After(chatId, afterId, userId, AppConstants.DefaultPageSize);
                var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

                if (result is not
                    { Success: true, Data.Messages.Count: > 0 })
                {
                    Debug.WriteLine($"[MessageManager] GapFill complete: {totalAdded}" +
                        $"messages in {batchesLoaded} batches");
                    return;
                }

                batchesLoaded++;

                await CacheMessagesOnlyAsync(result.Data.Messages);

                await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var members = _getMembersFunc();
                        var startIndex = Messages.Count;
                        var addedCount = 0;

                        foreach (var msg in result.Data.Messages)
                        {
                            if (!_loadedMessageIds.Add(msg.Id))
                                continue;

                            var vm = CreateMessageViewModel(msg, members);
                            if (msg.SenderId != userId)
                                vm.IsUnread = true;

                            Messages.Add(vm);
                            addedCount++;
                        }

                        if (addedCount > 0)
                        {
                            UpdateBounds();
                            UpdateDateSeparators();
                            for (int i = Math.Max(0, startIndex - 1); i < Messages.Count; i++)
                            {
                                UpdateGroupingForIndex(i);
                            }

                            totalAdded += addedCount;
                        }

                        _hasMoreNewer = result.Data.HasNewerMessages;
                    });

                await UpdateSyncStateAsync();

                if (!result.Data.HasNewerMessages || result.Data.Messages.Count < AppConstants.DefaultPageSize)
                {
                    Debug.WriteLine($"[MessageManager] GapFill complete: {totalAdded} messages in {batchesLoaded} batches");
                    return;
                }
            }

            Debug.WriteLine($"[MessageManager] GapFill limit reached ({MaxGapFillBatches} batches, " +
                $"{totalAdded} messages). Resetting chat to latest messages.");

            await ResetToLatestAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Чат закрыли
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] GapFill error: {ex.Message}");
        }
    }

    private async Task ResetToLatestAsync(CancellationToken ct)
    {
        try
        {
            if (cacheService != null)
            {
                await cacheService.ClearChatMessagesAsync(chatId);
            }

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

                await SaveToCacheAsync(
                    result.Data.Messages,
                    result.Data.HasMoreMessages, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Reset error: {ex.Message}");
        }
    }

    public void AddReceivedMessage(MessageDto message)
    {
        if (!_loadedMessageIds.Add(message.Id))
            return;

        var members = _getMembersFunc();
        var vm = CreateMessageViewModel(message, members);

        if (message.SenderId != userId)
        {
            vm.IsUnread = true;
        }

        Messages.Add(vm);
        UpdateBounds();
        UpdateDateSeparatorForNewMessage(vm);

        var index = Messages.Count - 1;
        MessageViewModel.UpdateGroupingAround(Messages, index);

        _hasMoreNewer = false;

        if (cacheService != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await cacheService.UpsertMessageAsync(message);
                    await UpdateSyncStateAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MessageManager] Cache write error: {ex.Message}");
                }
            });
        }
    }

    private async Task UpdateSyncStateAsync()
    {
        if (cacheService == null) return;

        try
        {
            await cacheService.UpdateSyncStateAsync(new ChatSyncState
            {
                ChatId = chatId,
                OldestLoadedId = _oldestLoadedMessageId,
                NewestLoadedId = _newestLoadedMessageId,
                HasMoreOlder = _hasMoreOlder,
                HasMoreNewer = _hasMoreNewer
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] SyncState update error: {ex.Message}");
        }
    }

    private async Task RevalidateNewestAsync(CancellationToken ct)
    {
        if (_newestLoadedMessageId == null) return;

        try
        {
            var url = ApiEndpoints.Messages.After(chatId, _newestLoadedMessageId.Value,userId, AppConstants.DefaultPageSize);
            var result = await _apiClient.GetAsync<PagedMessagesDto>(url, ct);

            if (result is not { Success: true, Data.Messages.Count: > 0 })
            {
                return;
            }

            await CacheMessagesOnlyAsync(result.Data.Messages);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var members = _getMembersFunc();
                    var startIndex = Messages.Count;

                    foreach (var msg in result.Data.Messages)
                    {
                        if (!_loadedMessageIds.Add(msg.Id)) continue;
                        Messages.Add(CreateMessageViewModel(msg, members));
                    }

                    if (Messages.Count > startIndex)
                    {
                        UpdateBounds();
                        UpdateDateSeparators();
                        for (int i = Math.Max(0, startIndex - 1); i < Messages.Count; i++)
                        {
                            UpdateGroupingForIndex(i);
                        }
                    }

                    _hasMoreNewer = result.Data.HasNewerMessages;
                });

            await UpdateSyncStateAsync();
        }
        catch (OperationCanceledException)
        {
            // Чат закрыли до завершения
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Revalidate failed: {ex.Message}");
        }
    }

    public void HandleMessageDeleted(int messageId)
    {
        var msg = Messages.FirstOrDefault(m => m.Id == messageId);
        if (msg == null) return;

        var index = Messages.IndexOf(msg);
        msg.MarkAsDeleted();

        MessageViewModel.UpdateGroupingAround(Messages, index);

        if (cacheService != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await cacheService.MarkMessageDeletedAsync(messageId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MessageManager] Cache delete error: {ex.Message}");
                }
            });
        }
    }

    public void HandleMessageUpdated(MessageDto updatedDto)
    {
        var msg = Messages.FirstOrDefault(m => m.Id == updatedDto.Id);
        if (msg == null) return;

        msg.ApplyUpdate(updatedDto);

        if (cacheService != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await cacheService.UpsertMessageAsync(updatedDto);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MessageManager] Cache update error: {ex.Message}");
                }
            });
        }
    }

    public void MarkAsReadLocally(int messageId)
    {
        foreach (var msg in Messages.Where(m => m.Id <= messageId && m.IsUnread))
        {
            msg.IsUnread = false;
        }

        if (!LastReadMessageId.HasValue || messageId > LastReadMessageId.Value)
        {
            LastReadMessageId = messageId;
        }
    }

    public IEnumerable<MessageViewModel> GetUnreadMessages()
        => Messages.Where(m => m.IsUnread && m.SenderId != userId);

    public int GetPollsCount()
        => Messages.Count(m => m.Poll != null);

    /// <summary>
    /// Рендерит список Dto в UI (очищает и пересоздаёт).
    /// Диспозит старые MessageFileViewModel.
    /// </summary>
    private void RenderMessages(List<MessageDto> messages)
    {
        DisposeAllMessages();

        Messages.Clear();
        _loadedMessageIds.Clear();

        var members = _getMembersFunc();
        foreach (var msg in messages)
        {
            if (_loadedMessageIds.Add(msg.Id))
            {
                Messages.Add(
                    CreateMessageViewModel(msg, members));
            }
        }

        UpdateBounds();
        UpdateDateSeparators();
        RecalculateGrouping();
    }

    /// <summary>
    /// Диспозит все MessageFileViewModel внутри сообщений.
    /// </summary>
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

    private async Task SaveToCacheAsync(List<MessageDto> messages, bool hasMoreOlder, bool hasMoreNewer)
    {
        if (cacheService == null || messages.Count == 0) return;

        try
        {
            await cacheService.UpsertMessagesAsync(messages);
            await cacheService.UpdateSyncStateAsync(new ChatSyncState
            {
                ChatId = chatId,
                OldestLoadedId = _oldestLoadedMessageId,
                NewestLoadedId = _newestLoadedMessageId,
                HasMoreOlder = hasMoreOlder,
                HasMoreNewer = hasMoreNewer
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Cache save error: {ex.Message}");
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
            Debug.WriteLine($"[MessageManager] Cache write error: {ex.Message}");
        }
    }

    private MessageViewModel CreateMessageViewModel(MessageDto msg, ObservableCollection<UserDto> members)
    {
        var sender = members.FirstOrDefault(m => m.Id == msg.SenderId);
        var vm = new MessageViewModel(msg, downloadService, notificationService)
        {
            SenderName = sender?.DisplayName ?? sender?.Username ?? msg.SenderName ?? "Unknown",
            SenderAvatar = sender?.Avatar ?? msg.SenderAvatarUrl
        };

        if (LastReadMessageId.HasValue && msg.Id > LastReadMessageId.Value && msg.SenderId != userId)
        {
            vm.IsUnread = true;
        }

        return vm;
    }

    #region Grouping

    private void RecalculateGrouping()
        => MessageViewModel.RecalculateGrouping(Messages);

    private void UpdateGroupingForIndex(int index)
    {
        if (index < 0 || index >= Messages.Count) return;

        var current = Messages[index];
        var prev = index > 0 ? Messages[index - 1] : null;
        var next = index < Messages.Count - 1 ? Messages[index + 1] : null;

        current.IsContinuation  = prev != null && MessageViewModel.CanGroup(prev, current);
        current.HasNextFromSame = next != null && MessageViewModel.CanGroup(current, next);
    }

    #endregion

    #region Bounds & Date Separators

    private void UpdateBounds()
    {
        if (Messages.Count > 0)
        {
            _oldestLoadedMessageId = Messages.Min(m => m.Id);
            _newestLoadedMessageId = Messages.Max(m => m.Id);
        }
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

        var previousMessage = Messages[index - 1];
        var previousDate = previousMessage.CreatedAt.Date;

        if (messageDate != previousDate)
        {
            newMessage.ShowDateSeparator = true;
            newMessage.DateSeparatorText =
                FormatDateSeparator(messageDate);
        }
        else
        {
            newMessage.ShowDateSeparator = false;
            newMessage.DateSeparatorText = null;
        }
    }

    private static string FormatDateSeparator(DateTime date)
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);

        if (date == today)
            return "Сегодня";

        if (date == yesterday)
            return "Вчера";

        if (date.Year == today.Year)
            return date.ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

        return date.ToString("d MMMM yyyy",
            System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    #endregion
}