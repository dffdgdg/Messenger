using MessengerDesktop.Data.Entities;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Cache;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.ReadReceipt;
using MessengerShared.DTO.User;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public class ChatMessageManager(
    int chatId,
    int userId,
    IApiClientService apiClient,
    Func<ObservableCollection<UserDTO>> getMembersFunc,
    IFileDownloadService? downloadService = null,
    INotificationService? notificationService = null,
    ILocalCacheService? cacheService = null)
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly Func<ObservableCollection<UserDTO>> _getMembersFunc = getMembersFunc ?? throw new ArgumentNullException(nameof(getMembersFunc));
    private readonly ILocalCacheService? _cache = cacheService;
    private int? _oldestLoadedMessageId;
    private int? _newestLoadedMessageId;
    private bool _hasMoreOlder = true;
    private bool _hasMoreNewer;
    private readonly HashSet<int> _loadedMessageIds = [];

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public bool IsLoading { get; private set; }
    public bool HasMoreOlder => _hasMoreOlder;
    public bool HasMoreNewer => _hasMoreNewer;
    public int? LastReadMessageId { get; private set; }
    public int? FirstUnreadMessageId { get; private set; }

    public void SetReadInfo(ChatReadInfoDTO? info)
    {
        if (info == null) return;

        LastReadMessageId = info.LastReadMessageId;
        FirstUnreadMessageId = info.FirstUnreadMessageId;

        Debug.WriteLine($"[MessageManager] ReadInfo: lastRead={LastReadMessageId}, firstUnread={FirstUnreadMessageId}");
    }

    public async Task<int?> LoadInitialMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading) return null;
        IsLoading = true;

        try
        {
            // Если есть непрочитанные — прыгаем к ним (around)
            if (FirstUnreadMessageId.HasValue)
            {
                return await LoadMessagesAroundInternalAsync(FirstUnreadMessageId.Value, ct);
            }

            // ═══ CACHE-FIRST: пробуем локальный кэш ═══
            if (_cache != null)
            {
                var cached = await _cache.GetMessagesAsync(chatId, AppConstants.DefaultPageSize);

                if (cached is { Messages.Count: > 0 })
                {
                    RenderMessages(cached.Messages);
                    _hasMoreOlder = cached.HasMoreOlder;
                    _hasMoreNewer = false;

                    var scrollIndex = Messages.Count > 0 ? Messages.Count - 1 : (int?)null;

                    Debug.WriteLine(
                        $"[MessageManager] Loaded {cached.Messages.Count} messages from CACHE for chat {chatId}");

                    // Фоновая ревалидация — подгрузить новые с сервера
                    _ = RevalidateNewestAsync(ct);

                    return scrollIndex;
                }
            }

            // ═══ NETWORK FALLBACK ═══
            return await LoadInitialFromServerAsync(ct);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Оригинальная логика загрузки с сервера + сохранение в кэш</summary>
    private async Task<int?> LoadInitialFromServerAsync(CancellationToken ct)
    {
        var url = ApiEndpoints.Message.ForChat(chatId, userId, 1, AppConstants.DefaultPageSize);
        var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

        if (result is { Success: true, Data: not null })
        {
            RenderMessages(result.Data.Messages);
            _hasMoreOlder = result.Data.HasMoreMessages;
            _hasMoreNewer = false;

            // cохраняем
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
            return await LoadMessagesAroundInternalAsync(messageId, ct);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<int?> LoadMessagesAroundInternalAsync(int messageId, CancellationToken ct = default)
    {
        if (_cache != null)
        {
            var cached = await _cache.GetMessagesAroundAsync(chatId, messageId, AppConstants.DefaultPageSize);

            if (cached is { IsComplete: true, Messages.Count: > 0 })
            {
                RenderMessages(cached.Messages);
                _hasMoreOlder = cached.HasMoreOlder;
                _hasMoreNewer = cached.HasMoreNewer;

                var targetIndex = Messages.ToList().FindIndex(m => m.Id == messageId);
                Debug.WriteLine(
                    $"[MessageManager] Loaded {Messages.Count} messages around {messageId} from CACHE, targetIndex={targetIndex}");
                return targetIndex >= 0 ? targetIndex : null;
            }
        }

        var url = ApiEndpoints.Message.Around(chatId, messageId, userId, AppConstants.DefaultPageSize);
        var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

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

            await SaveToCacheAsync(result.Data.Messages, result.Data.HasMoreMessages, result.Data.HasNewerMessages);

            Debug.WriteLine(
                $"[MessageManager] Loaded {Messages.Count} messages around {messageId} from SERVER, targetIndex={targetIndex}");
            return targetIndex;
        }

        Debug.WriteLine($"[MessageManager] Failed to load messages around {messageId}");
        return null;
    }

    public async Task LoadOlderMessagesAsync(CancellationToken ct = default)
    {
        if (IsLoading || !_hasMoreOlder || !_oldestLoadedMessageId.HasValue) return;
        IsLoading = true;

        try
        {
            const int requestedCount = AppConstants.LoadMorePageSize;
            List<MessageDTO>? messagesToPrepend = null;
            bool hasMore;

            // ═══ CACHE-FIRST ═══
            if (_cache != null)
            {
                var cached = await _cache.GetMessagesBeforeAsync(
                    chatId, _oldestLoadedMessageId.Value, requestedCount);

                if (cached is { IsComplete: true, Messages.Count: > 0 })
                {
                    // Кэш полностью покрывает запрос
                    messagesToPrepend = cached.Messages;
                    hasMore = cached.HasMoreOlder;
                    Debug.WriteLine(
                        $"[MessageManager] Loaded {messagesToPrepend.Count} older from CACHE");
                }
                else
                {
                    // Кэша недостаточно — дозагружаем с сервера
                    var cacheCount = cached?.Messages.Count ?? 0;
                    var serverBeforeId = cacheCount > 0
                        ? cached!.Messages.Min(m => m.Id)
                        : _oldestLoadedMessageId.Value;

                    var url = ApiEndpoints.Message.Before(
                        chatId, serverBeforeId, userId, requestedCount - cacheCount);
                    var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

                    if (result is { Success: true, Data: not null })
                    {
                        // Сохраняем серверные в кэш
                        await CacheMessagesOnlyAsync(result.Data.Messages);

                        // Объединяем: кэш + сервер, дедупликация, сортировка
                        messagesToPrepend = [.. (cached?.Messages ?? [])
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
                // ═══ ORIGINAL LOGIC (no cache) ═══
                var url = ApiEndpoints.Message.Before(
                    chatId, _oldestLoadedMessageId.Value, userId, requestedCount);
                var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

                if (result is not { Success: true, Data: not null }) return;

                messagesToPrepend = result.Data.Messages;
                hasMore = result.Data.HasMoreMessages;
            }

            // ═══ ADD TO UI ═══
            if (messagesToPrepend is { Count: > 0 })
            {
                var members = _getMembersFunc();

                for (int i = messagesToPrepend.Count - 1; i >= 0; i--)
                {
                    var msg = messagesToPrepend[i];
                    if (!_loadedMessageIds.Add(msg.Id)) continue; // дедупликация
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
        if (IsLoading || !_hasMoreNewer || !_newestLoadedMessageId.HasValue) return;
        IsLoading = true;

        try
        {
            List<MessageDTO>? messagesToAppend = null;
            bool hasNewer;

            // ═══ CACHE-FIRST ═══
            if (_cache != null)
            {
                var cached = await _cache.GetMessagesAfterAsync(
                    chatId, _newestLoadedMessageId.Value, AppConstants.LoadMorePageSize);

                if (cached is { IsComplete: true, Messages.Count: > 0 })
                {
                    messagesToAppend = cached.Messages;
                    hasNewer = cached.HasMoreNewer;
                    Debug.WriteLine(
                        $"[MessageManager] Loaded {messagesToAppend.Count} newer from CACHE");
                }
                else
                {
                    // Дозагрузка с сервера
                    var url = ApiEndpoints.Message.After(
                        chatId, _newestLoadedMessageId.Value, userId, AppConstants.LoadMorePageSize);
                    var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

                    if (result is not { Success: true, Data: not null }) return;

                    await CacheMessagesOnlyAsync(result.Data.Messages);
                    messagesToAppend = result.Data.Messages;
                    hasNewer = result.Data.HasNewerMessages;
                }
            }
            else
            {
                // ═══ ORIGINAL LOGIC ═══
                var url = ApiEndpoints.Message.After(
                    chatId, _newestLoadedMessageId.Value, userId, AppConstants.LoadMorePageSize);
                var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

                if (result is not { Success: true, Data: not null }) return;

                messagesToAppend = result.Data.Messages;
                hasNewer = result.Data.HasNewerMessages;
            }

            // ═══ ADD TO UI ═══
            if (messagesToAppend is { Count: > 0 })
            {
                var members = _getMembersFunc();
                var startIndex = Messages.Count;

                foreach (var msg in messagesToAppend)
                {
                    if (!_loadedMessageIds.Add(msg.Id)) continue;
                    Messages.Add(CreateMessageViewModel(msg, members));
                }

                UpdateBounds();
                UpdateDateSeparators();

                if (startIndex > 0)
                {
                    for (int i = Math.Max(0, startIndex - 1); i < Messages.Count; i++)
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

    /// <summary>
    /// Gap fill после reconnect: загружает сообщения, пришедшие пока связь была потеряна.
    /// Вызывается из ChatViewModel при событии Reconnected.
    /// </summary>
    public async Task GapFillAfterReconnectAsync(CancellationToken ct = default)
    {
        if (_newestLoadedMessageId == null) return;

        try
        {
            var url = ApiEndpoints.Message.After(
                chatId, _newestLoadedMessageId.Value, userId, AppConstants.DefaultPageSize);
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result is not { Success: true, Data.Messages.Count: > 0 })
            {
                Debug.WriteLine("[MessageManager] GapFill: no missed messages");
                return;
            }

            // Сохраняем в кэш
            await CacheMessagesOnlyAsync(result.Data.Messages);

            // Добавляем в UI на UI-потоке
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var members = _getMembersFunc();
                var startIndex = Messages.Count;
                var addedCount = 0;

                foreach (var msg in result.Data.Messages)
                {
                    if (!_loadedMessageIds.Add(msg.Id)) continue;

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

                    Debug.WriteLine(
                        $"[MessageManager] GapFill: added {addedCount} missed messages");
                }

                _hasMoreNewer = result.Data.HasNewerMessages;
            });

            await UpdateSyncStateAsync();

            // Если ещё есть — рекурсивно догружаем (но с лимитом)
            if (result.Data.HasNewerMessages && result.Data.Messages.Count >= AppConstants.DefaultPageSize)
            {
                Debug.WriteLine("[MessageManager] GapFill: more messages available, loading next batch");
                await GapFillAfterReconnectAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] GapFill error: {ex.Message}");
        }
    }

    public void AddReceivedMessage(MessageDTO message)
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

        if (_cache != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _cache.UpsertMessageAsync(message);
                    await UpdateSyncStateAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MessageManager] Cache write error: {ex.Message}");
                }
            });
        }
    }

    /// <summary>Обновляет SyncState в кэше с текущими границами</summary>
    private async Task UpdateSyncStateAsync()
    {
        if (_cache == null) return;

        try
        {
            await _cache.UpdateSyncStateAsync(new ChatSyncState
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

    /// <summary>
    /// Фоновая ревалидация: проверяет есть ли новые сообщения после последнего кэшированного.
    /// Если есть — догружает и добавляет в UI.
    /// </summary>
    private async Task RevalidateNewestAsync(CancellationToken ct)
    {
        if (_newestLoadedMessageId == null) return;

        try
        {
            var url = ApiEndpoints.Message.After(
                chatId, _newestLoadedMessageId.Value, userId, AppConstants.DefaultPageSize);
            var result = await _apiClient.GetAsync<PagedMessagesDTO>(url, ct);

            if (result is not { Success: true, Data.Messages.Count: > 0 })
            {
                Debug.WriteLine("[MessageManager] Revalidate: no new messages");
                return;
            }

            // Сохраняем в кэш
            await CacheMessagesOnlyAsync(result.Data.Messages);

            // Добавляем в UI
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

            Debug.WriteLine(
                $"[MessageManager] Revalidated: added {result.Data.Messages.Count} new messages");
        }
        catch (OperationCanceledException)
        {
            // Чат закрыли до завершения ревалидации — нормально
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

        if (_cache != null)
        {
            _ = Task.Run(async () =>
            {
                try { await _cache.MarkMessageDeletedAsync(messageId); }
                catch (Exception ex) { Debug.WriteLine($"[MessageManager] Cache delete error: {ex.Message}"); }
            });
        }
    }

    public void HandleMessageUpdated(MessageDTO updatedDto)
    {
        var msg = Messages.FirstOrDefault(m => m.Id == updatedDto.Id);
        if (msg == null) return;

        msg.ApplyUpdate(updatedDto);

        Debug.WriteLine($"[MessageManager] Message {updatedDto.Id} updated in UI");

        if (_cache != null)
        {
            _ = Task.Run(async () =>
            {
                try { await _cache.UpsertMessageAsync(updatedDto); }
                catch (Exception ex) { Debug.WriteLine($"[MessageManager] Cache update error: {ex.Message}"); }
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

    public int GetPollsCount() => Messages.Count(m => m.Poll != null);

    /// <summary>Рендерит список DTO в UI (очищает и пересоздаёт)</summary>
    private void RenderMessages(List<MessageDTO> messages)
    {
        Messages.Clear();
        _loadedMessageIds.Clear();

        var members = _getMembersFunc();
        foreach (var msg in messages)
        {
            if (_loadedMessageIds.Add(msg.Id))
            {
                Messages.Add(CreateMessageViewModel(msg, members));
            }
        }

        UpdateBounds();
        UpdateDateSeparators();
        RecalculateGrouping();
    }

    /// <summary>Сохраняет сообщения в кэш + обновляет SyncState</summary>
    private async Task SaveToCacheAsync(List<MessageDTO> messages, bool hasMoreOlder, bool hasMoreNewer)
    {
        if (_cache == null || messages.Count == 0) return;

        try
        {
            await _cache.UpsertMessagesAsync(messages);
            await _cache.UpdateSyncStateAsync(new ChatSyncState
            {
                ChatId = chatId,
                OldestLoadedId = _oldestLoadedMessageId,
                NewestLoadedId = _newestLoadedMessageId,
                HasMoreOlder = hasMoreOlder,
                HasMoreNewer = hasMoreNewer
            });

            Debug.WriteLine(
                $"[MessageManager] Cached {messages.Count} messages for chat {chatId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Cache save error: {ex.Message}");
        }
    }

    /// <summary>Сохраняет сообщения в кэш БЕЗ обновления SyncState</summary>
    private async Task CacheMessagesOnlyAsync(List<MessageDTO> messages)
    {
        if (_cache == null || messages.Count == 0) return;

        try
        {
            await _cache.UpsertMessagesAsync(messages);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Cache write error: {ex.Message}");
        }
    }


    private MessageViewModel CreateMessageViewModel(MessageDTO msg, ObservableCollection<UserDTO> members)
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

    /// <summary>
    /// Полный пересчёт группировки для всех сообщений
    /// </summary>
    private void RecalculateGrouping()
    {
        MessageViewModel.RecalculateGrouping(Messages);
    }

    /// <summary>
    /// Пересчёт группировки для одного индекса
    /// </summary>
    private void UpdateGroupingForIndex(int index)
    {
        if (index < 0 || index >= Messages.Count) return;

        var current = Messages[index];
        var prev = index > 0 ? Messages[index - 1] : null;
        var next = index < Messages.Count - 1 ? Messages[index + 1] : null;

        current.IsContinuation = prev != null && MessageViewModel.CanGroup(prev, current);
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
            newMessage.DateSeparatorText = FormatDateSeparator(messageDate);
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

        return date.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    #endregion
}