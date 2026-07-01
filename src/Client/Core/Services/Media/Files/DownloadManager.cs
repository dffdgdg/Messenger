using Core.Services.Media.Abstractions;
using Core.Shared.Configuration;
using Core.Shared.Helpers;
using Shared.Contracts.Message;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Core.Services.Media.Files;

public sealed class DownloadManager(IFileDownloadService fileDownloadService, IFileDownloadStateService stateService)
    : IDownloadManager, IDisposable
{
    private readonly SemaphoreSlim _concurrencyGate = new(AppConstants.MaxConcurrentDownloads);
    private readonly ConcurrentDictionary<int, Lazy<DownloadSlot>> _active = new();
    private bool _disposed;

    public async Task<DownloadOutcome> DownloadAsync(MessageFileDto file, IProgress<DownloadProgressInfo>? progress = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(file);

        if (string.IsNullOrEmpty(file.Url))
            return new DownloadOutcome(DownloadOutcomeStatus.Failed, null, "У файла отсутствует ссылка на скачивание");

        // LazyThreadSafetyMode.ExecutionAndPublication гарантирует, что фабрика
        // будет вызвана ровно один раз, даже если несколько потоков одновременно попадут в GetOrAdd.
        var lazySlot = _active.GetOrAdd(file.Id, _ => new Lazy<DownloadSlot>(() => CreateSlot(file), LazyThreadSafetyMode.ExecutionAndPublication));
        var slot = lazySlot.Value;

        void Handler(DownloadProgressInfo info) => progress?.Report(info);

        if (progress is not null)
            slot.ProgressChanged += Handler;

        try
        {
            // WaitAsync(ct) — отмена ЭТОГО конкретного вызова не останавливает саму закачку,
            // если её ждут другие подписчики.
            return await slot.Task.WaitAsync(ct);
        }
        finally
        {
            if (progress is not null)
                slot.ProgressChanged -= Handler;
        }
    }

    public void CancelDownload(int fileId)
    {
        if (_active.TryGetValue(fileId, out var lazySlot) && lazySlot.IsValueCreated)
        {
            try { lazySlot.Value.Cts.Cancel(); }
            catch (ObjectDisposedException) { /* уже завершилось */ }
        }
    }

    public bool IsDownloading(int fileId) => _active.ContainsKey(fileId);

    private DownloadSlot CreateSlot(MessageFileDto file)
    {
        var cts = new CancellationTokenSource();
        var slot = new DownloadSlot(cts);
        slot.Task = RunDownloadAsync(file, slot, cts.Token);
        return slot;
    }

    private async Task<DownloadOutcome> RunDownloadAsync(MessageFileDto file, DownloadSlot slot, CancellationToken ct)
    {
        try
        {
            await _concurrencyGate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Cleanup(file.Id, slot);
            return new DownloadOutcome(DownloadOutcomeStatus.Cancelled, null, null);
        }

        try
        {
            var destinationPath = BuildCachePath(file);

            var adapter = new Progress<FileFetchProgress>(p =>
                slot.ReportProgress(new DownloadProgressInfo(p.Percentage, p.BytesReceived, p.TotalBytes)));

            var result = await fileDownloadService.FetchAsync(file.Url!, destinationPath, adapter, ct);

            if (result.Status != FileFetchStatus.Completed)
                return new DownloadOutcome(DownloadOutcomeStatus.Cancelled, null, null);

            try { await stateService.RegisterDownloadAsync(file, result.FilePath); }
            catch (Exception ex) { Debug.WriteLine($"[DownloadManager] RegisterDownloadAsync error: {ex.Message}"); }

            return new DownloadOutcome(DownloadOutcomeStatus.Completed, result.FilePath, null);
        }
        catch (OperationCanceledException)
        {
            return new DownloadOutcome(DownloadOutcomeStatus.Cancelled, null, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DownloadManager] Download failed for file {file.Id}: {ex.Message}");
            return new DownloadOutcome(DownloadOutcomeStatus.Failed, null, ex.Message);
        }
        finally
        {
            _concurrencyGate.Release();
            Cleanup(file.Id, slot);
        }
    }

    private void Cleanup(int fileId, DownloadSlot slot)
    {
        _active.TryRemove(fileId, out _);
        slot.Cts.Dispose();
    }

    /// <summary>
    /// Имя файла в кэше детерминировано по Id вложения на сервере.
    /// Это устраняет саму возможность гонки в генерации уникальных имён
    /// (двум разным файлам физически не может достаться один путь), и даёт
    /// стабильный путь для докачки (.part) при повторных попытках.
    /// </summary>
    private static string BuildCachePath(MessageFileDto file)
    {
        var cacheDir = AppPaths.GetFileCacheDirectory();
        var extension = Path.GetExtension(file.FileName);
        return Path.Combine(cacheDir, $"{file.Id}{extension}");
    }

    private sealed class DownloadSlot(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public Task<DownloadOutcome> Task { get; set; } = null!;
        public event Action<DownloadProgressInfo>? ProgressChanged;
        public void ReportProgress(DownloadProgressInfo info) => ProgressChanged?.Invoke(info);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var lazy in _active.Values)
        {
            if (!lazy.IsValueCreated) continue;
            try { lazy.Value.Cts.Cancel(); } catch { /* ignore */ }
        }

        _concurrencyGate.Dispose();
    }
}