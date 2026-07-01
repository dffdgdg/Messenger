using Core.Data.Models.Sync;
using Core.Data.Repositories.Abstractions;
using Core.Infrastructure;
using Core.Services.Api.Abstraction;
using Core.Shared.Configuration;
using Shared.Contracts.Message;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Core.Features.MessageList.ViewModels.Managers;

/// <summary>
/// Загружает страницы сообщений с сервера и из кэша.
/// </summary>
public sealed class MessageLoader : IDisposable
{
    private const int MaxGapFillBatches = 5;
    private const int DefaultPage = AppConstants.DefaultPageSize;
    private const int LoadMorePage = AppConstants.LoadMorePageSize;
    private const int CacheRevalidationThresholdSeconds = 30;

    private readonly IApiClientService _api;
    private readonly ILocalCacheService? _cache;
    private readonly MessageCollection _collection;
    private readonly int _chatId;
    private readonly int _userId;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Lock _bgLock = new();
    private readonly List<Task> _backgroundTasks = [];

    private int _isLoading;

    public bool IsLoading => Volatile.Read(ref _isLoading) != 0;

    public MessageLoader(
        IApiClientService api,
        ILocalCacheService? cache,
        MessageCollection collection,
        int chatId,
        int userId)
    {
        _api = api;
        _cache = cache;
        _collection = collection;
        _chatId = chatId;
        _userId = userId;
    }

    public Task<int?> LoadInitialAsync(int? targetId, CancellationToken ct)
        => WithLoadingGuardAsync(() => LoadInitialCoreAsync(targetId, ct));

    public Task<int?> LoadAroundAsync(int messageId, CancellationToken ct)
        => WithLoadingGuardAsync(() => LoadAroundCoreAsync(messageId, ct));

    public Task LoadOlderAsync(CancellationToken ct)
        => WithLoadingGuardVoidAsync(() => LoadPageAsync(Direction.Older, ct));

    public Task LoadNewerAsync(CancellationToken ct)
        => WithLoadingGuardVoidAsync(() => LoadPageAsync(Direction.Newer, ct));

    public Task GapFillAsync(CancellationToken ct)
    {
        if (_collection.NewestId == null || !TryBeginLoading()) return Task.CompletedTask;
        return GapFillLoopAsync(ct).ContinueWith(_ => EndLoading());
    }

    public Task ResetToLatestAsync(CancellationToken ct)
        => ResetCoreAsync(ct);

    private async Task<int?> LoadInitialCoreAsync(int? targetId, CancellationToken ct)
    {
        if (targetId.HasValue) return await LoadAroundCoreAsync(targetId.Value, ct);
        if (_collection.FirstUnreadMessageId.HasValue)
            return await LoadAroundCoreAsync(_collection.FirstUnreadMessageId.Value, ct);
        if (await TryLoadFromCacheAsync() is { } cached) return cached;
        return await LoadInitialFromServerAsync(ct);
    }

    private async Task<int?> TryLoadFromCacheAsync()
    {
        if (_cache == null) return null;

        var cached = await Task.Run(() => _cache.GetMessagesAsync(_chatId, DefaultPage));
        if (cached is not { Messages.Count: >= DefaultPage / 2 }) return null;

        var syncState = await _cache.GetSyncStateAsync(_chatId);

        if (syncState?.HasMoreOlder == false && syncState.OldestLoadedId.HasValue)
        {
            var actualOldest = cached.Messages.Count > 0 ? cached.Messages[0].Id : (int?)null;
            if (actualOldest > syncState.OldestLoadedId.Value)
            {
                Debug.WriteLine("[Loader] Кэш рассинхронизирован, очищаем");
                try { await _cache.ClearChatMessagesAsync(_chatId); }
                catch (Exception ex) { Debug.WriteLine($"[Loader] Clear cache error: {ex.Message}"); }
                return await LoadInitialFromServerAsync(CancellationToken.None);
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _collection.Render(cached.Messages);
            _collection.SetBounds(cached.HasMoreOlder, false);
        });

        Debug.WriteLine($"[Loader] Loaded {cached.Messages.Count} from cache");

        if (syncState != null &&
            (DateTime.UtcNow - syncState.LastSyncAt).TotalSeconds > CacheRevalidationThresholdSeconds)
        {
            RunInBackground(() => RevalidateNewestAsync(_disposeCts.Token));
        }

        return _collection.LastIndex;
    }

    private async Task<int?> LoadInitialFromServerAsync(CancellationToken ct)
    {
        var data = await FetchAsync(ApiEndpoints.Messages.Latest(_chatId, DefaultPage), ct);
        if (data == null) return null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _collection.Render(data.Messages);
            _collection.SetBounds(data.HasMoreMessages, false);
        });

        await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, false);
        return _collection.LastIndex;
    }

    private async Task<int?> LoadAroundCoreAsync(int messageId, CancellationToken ct)
    {
        if (_cache != null)
        {
            var cached = await Task.Run(
                () => _cache.GetMessagesAroundAsync(_chatId, messageId, DefaultPage), ct);

            if (cached is { IsComplete: true, Messages.Count: > 0 })
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _collection.Render(cached.Messages);
                    _collection.SetBounds(cached.HasMoreOlder, cached.HasMoreNewer);
                });
                return _collection.FindIndex(messageId);
            }
        }

        var data = await FetchAsync(
            ApiEndpoints.Messages.Around(_chatId, messageId, _userId, DefaultPage), ct);
        if (data == null) return null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _collection.Render(data.Messages);
            _collection.SetBounds(data.HasMoreMessages, data.HasNewerMessages);
        });

        await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, data.HasNewerMessages);
        return _collection.FindIndex(messageId);
    }

    private async Task LoadPageAsync(Direction dir, CancellationToken ct)
    {
        if (!GetHasMore(dir) || GetAnchor(dir) is not { } anchorId) return;

        var page = await LoadDirectionalAsync(dir, anchorId, LoadMorePage, ct);
        if (page == null || page.Messages.Count == 0)
        {
            SetHasMore(dir, false);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (dir == Direction.Older)
            {
                _collection.Prepend(page.Messages);
                _collection.HasMoreOlder = page.HasMore;
            }
            else
            {
                _collection.Append(page.Messages);
                _collection.HasMoreNewer = page.HasMore;
            }
        });

        await SafeUpdateSyncStateAsync();
    }

    private async Task<DirectionalPage?> LoadDirectionalAsync(
        Direction dir, int anchorId, int count, CancellationToken ct)
    {
        if (_cache != null)
        {
            var fromCache = await TryDirectionalFromCacheAsync(dir, anchorId, count, ct);
            if (fromCache != null) return fromCache;
        }
        return await DirectionalFromServerAsync(dir, anchorId, count, ct);
    }

    private async Task<DirectionalPage?> TryDirectionalFromCacheAsync(
        Direction dir, int anchorId, int count, CancellationToken ct)
    {
        var cached = dir == Direction.Older
            ? await Task.Run(() => _cache!.GetMessagesBeforeAsync(_chatId, anchorId, count))
            : await Task.Run(() => _cache!.GetMessagesAfterAsync(_chatId, anchorId, count));

        if (cached is { IsComplete: true, Messages.Count: > 0 })
            return new(cached.Messages, GetCachedHasMore(cached, dir));

        if (dir == Direction.Older && cached?.Messages is { Count: > 0 } partial)
        {
            var serverAnchor = partial.Min(m => m.Id);
            var data = await FetchAsync(
                ApiEndpoints.Messages.Before(_chatId, serverAnchor, _userId, count - partial.Count), ct);
            if (data == null) return null;

            await SafeCacheMessagesAsync(data.Messages);
            return new(Merge(partial, data.Messages), data.HasMoreMessages);
        }

        return null;
    }

    private async Task<DirectionalPage?> DirectionalFromServerAsync(
        Direction dir, int anchorId, int count, CancellationToken ct)
    {
        var data = await FetchAsync(BuildUrl(dir, anchorId, count), ct);
        if (data == null) return null;
        await SafeCacheMessagesAsync(data.Messages);
        return new(data.Messages, GetServerHasMore(data, dir));
    }

    private async Task GapFillLoopAsync(CancellationToken ct)
    {
        try
        {
            int batches = 0, totalAdded = 0;

            while (batches < MaxGapFillBatches)
            {
                ct.ThrowIfCancellationRequested();

                var data = await FetchAsync(
                    ApiEndpoints.Messages.After(_chatId, _collection.NewestId!.Value, _userId, DefaultPage), ct);

                if (data is not { Messages.Count: > 0 }) break;

                batches++;
                await SafeCacheMessagesAsync(data.Messages);

                int added = 0;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var before = _collection.Messages.Count;
                    _collection.Append(data.Messages);
                    _collection.HasMoreNewer = data.HasNewerMessages;
                    added = _collection.Messages.Count - before;
                });

                totalAdded += added;
                await SafeUpdateSyncStateAsync();

                if (!data.HasNewerMessages || data.Messages.Count < DefaultPage) break;
            }

            if (batches >= MaxGapFillBatches)
            {
                Debug.WriteLine($"[Loader] GapFill limit hit ({batches} batches, +{totalAdded}), resetting");
                await ResetCoreAsync(ct);
            }
            else
            {
                Debug.WriteLine($"[Loader] GapFill: +{totalAdded} in {batches} batches");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[Loader] GapFill error: {ex.Message}"); }
    }

    private async Task ResetCoreAsync(CancellationToken ct)
    {
        try
        {
            if (_cache != null) await _cache.ClearChatMessagesAsync(_chatId);

            await Dispatcher.UIThread.InvokeAsync(() => _collection.Render([]));

            var data = await FetchAsync(ApiEndpoints.Messages.Latest(_chatId, DefaultPage), ct);
            if (data == null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _collection.Render(data.Messages);
                _collection.SetBounds(data.HasMoreMessages, false);
            });

            await SafeSaveToCacheAsync(data.Messages, data.HasMoreMessages, false);
        }
        catch (Exception ex) { Debug.WriteLine($"[Loader] Reset error: {ex.Message}"); }
    }

    private async Task RevalidateNewestAsync(CancellationToken ct)
    {
        if (_collection.NewestId == null) return;
        try
        {
            var data = await FetchAsync(
                ApiEndpoints.Messages.After(_chatId, _collection.NewestId.Value, _userId, DefaultPage), ct);

            if (data is not { Messages.Count: > 0 }) return;

            await SafeCacheMessagesAsync(data.Messages);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _collection.Append(data.Messages);
                _collection.HasMoreNewer = data.HasNewerMessages;
            });
            await SafeUpdateSyncStateAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[Loader] Revalidate error: {ex.Message}"); }
    }

    private async Task<PagedMessagesDto?> FetchAsync(string url, CancellationToken ct)
    {
        Debug.WriteLine($"[Loader] GET {url}");
        if (ct.IsCancellationRequested) return null;
        var result = await _api.GetAsync<PagedMessagesDto>(url, ct);
        Debug.WriteLine($"[Loader] → success={result?.Success} count={result?.Data?.Messages?.Count ?? -1}");
        return result is { Success: true, Data: not null } ? result.Data : null;
    }

    private Task SafeCacheMessagesAsync(List<MessageDto> msgs)
        => msgs.Count > 0 && _cache != null
            ? SafeCacheAsync(() => _cache.UpsertMessagesAsync(msgs))
            : Task.CompletedTask;

    private Task SafeUpdateSyncStateAsync()
        => _cache != null
            ? SafeCacheAsync(() => _cache.UpdateSyncStateAsync(BuildSyncState()))
            : Task.CompletedTask;

    private async Task SafeSaveToCacheAsync(List<MessageDto> msgs, bool hasOlder, bool hasNewer)
    {
        if (_cache == null || msgs.Count == 0) return;
        await SafeCacheAsync(async () =>
        {
            await _cache.UpsertMessagesAsync(msgs);
            await _cache.UpdateSyncStateAsync(BuildSyncState(hasOlder, hasNewer));
        });
    }

    private static async Task SafeCacheAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Debug.WriteLine($"[Loader] Cache error: {ex.Message}"); }
    }

    private ChatSyncState BuildSyncState(bool? hasOlder = null, bool? hasNewer = null) => new()
    {
        ChatId = _chatId,
        OldestLoadedId = _collection.OldestId,
        NewestLoadedId = _collection.NewestId,
        HasMoreOlder = hasOlder ?? _collection.HasMoreOlder,
        HasMoreNewer = hasNewer ?? _collection.HasMoreNewer
    };

    private enum Direction { Older, Newer }
    private sealed record DirectionalPage(List<MessageDto> Messages, bool HasMore);

    private int? GetAnchor(Direction d)
        => d == Direction.Older ? _collection.OldestId : _collection.NewestId;

    private bool GetHasMore(Direction d)
        => d == Direction.Older ? _collection.HasMoreOlder : _collection.HasMoreNewer;

    private void SetHasMore(Direction d, bool value)
    {
        if (d == Direction.Older) _collection.HasMoreOlder = value;
        else _collection.HasMoreNewer = value;
    }

    private string BuildUrl(Direction d, int anchor, int count) =>
        d == Direction.Older
            ? ApiEndpoints.Messages.Before(_chatId, anchor, _userId, count)
            : ApiEndpoints.Messages.After(_chatId, anchor, _userId, count);

    private static bool GetCachedHasMore(CachedMessagesResult c, Direction d)
        => d == Direction.Older ? c.HasMoreOlder : c.HasMoreNewer;

    private static bool GetServerHasMore(PagedMessagesDto p, Direction d)
        => d == Direction.Older ? p.HasMoreMessages : p.HasNewerMessages;

    private static List<MessageDto> Merge(List<MessageDto>? a, List<MessageDto> b)
        => [.. (a ?? []).Concat(b).GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.Id)];

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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            { Debug.WriteLine($"[Loader] Background error in {caller}: {ex.Message}"); }
        }, token);

        lock (_bgLock)
        {
            _backgroundTasks.RemoveAll(t => t.IsCompleted);
            _backgroundTasks.Add(task);
        }
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

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();

        Task[] pending;
        lock (_bgLock) pending = [.. _backgroundTasks];

        try { await Task.WhenAll(pending); }
        catch (Exception ex)
        { Debug.WriteLine($"[Loader] DisposeAsync bg tasks error: {ex.Message}"); }

        _disposeCts.Dispose();
    }

    public void Dispose() => _disposeCts.Cancel();
}