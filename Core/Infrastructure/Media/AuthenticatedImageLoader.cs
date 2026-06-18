using Core.Infrastructure.Diagnostics;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Core.Infrastructure.Media;

public sealed class AuthenticatedImageLoader : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ISessionStore _sessionStore;
    private readonly string _apiBaseUrl;
    private readonly string _cacheDirectory;

    private readonly LinkedList<(string Key, byte[] Data)> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, byte[] Data)>> _lruMap = [];
    private readonly Lock _lruLock = new();
    private const int MaxRamCacheItems = 80;
    private long _ramCacheBytes;
    private const long MaxRamCacheBytes = 30L * 1024 * 1024;
    private const long LohThresholdBytes = 85 * 1024;

    private readonly Dictionary<string, (Task<byte[]?> Task, CancellationTokenSource Cts)> _inflight = [];

    private static readonly HashSet<string> ImageExtensions
        = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".ico", ".avif" };

    public AuthenticatedImageLoader(HttpClient httpClient, ISessionStore sessionStore, string apiBaseUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _apiBaseUrl = apiBaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(apiBaseUrl));

        _cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Desktop", "ImageCache");
        Directory.CreateDirectory(_cacheDirectory);

        MemoryDiagnostics.RegisterImageLoader(getStats: () => { lock (_lruLock) return (_lruMap.Count, _ramCacheBytes); },
            clearCache: ClearCache);
        MemoryDiagnostics.RegisterDiskCacheDir(_cacheDirectory);
    }

    public async Task<byte[]?> GetBytesAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var ext = GetExtension(url);
        if (!string.IsNullOrEmpty(ext) && !ImageExtensions.Contains(ext))
        {
            Debug.WriteLine($"[AuthImageLoader] Skipping non-image: {GetFileName(url)}");
            return null;
        }

        lock (_lruLock)
        {
            if (GetFromRamCacheUnsafe(url) is { } cachedBytes)
                return cachedBytes;
        }

        Task<byte[]?> loadTask;
        bool isOwner;

        lock (_lruLock)
        {
            if (_inflight.TryGetValue(url, out var existing))
            {
                loadTask = existing.Task;
                isOwner = false;
            }
            else
            {
                var cts = new CancellationTokenSource();
                loadTask = LoadBytesWithCtsAsync(url, ext, cts);
                _inflight[url] = (loadTask, cts);
                isOwner = true;
            }
        }

        try
        {
            // Ожидаем с CancellationToken вызывающего через WaitAsync
            // Если вызывающий отменился — только он получит OperationCanceledException,
            // остальные продолжат ждать оригинальный Task
            return await loadTask.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Отмена только для текущего вызывающего — не влияет на других
            return null;
        }
        finally
        {
            // Только владелец чистит _inflight и диспозит CTS
            if (isOwner)
            {
                lock (_lruLock)
                {
                    if (_inflight.TryGetValue(url, out var entry) && entry.Task == loadTask)
                    {
                        _inflight.Remove(url);
                    }
                }
            }
        }
    }

    private async Task<byte[]?> LoadBytesWithCtsAsync(string url, string ext, CancellationTokenSource cts)
    {
        try
        {
            return await LoadBytesAsync(url, ext, cts.Token);
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task<byte[]?> LoadBytesAsync(string url, string ext, CancellationToken ct)
    {
        byte[]? data;

        lock (_lruLock)
            data = GetFromRamCacheUnsafe(url);

        if (data == null)
        {
            var diskPath = GetDiskCachePath(url, ext);
            if (File.Exists(diskPath))
            {
                try
                {
                    MemoryDiagnostics.OnImageDiskHit();
                    data = await File.ReadAllBytesAsync(diskPath, ct);
                    lock (_lruLock)
                        PutToRamCacheUnsafe(url, data);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AuthImageLoader] Disk read failed: {ex.Message}");
                }
            }
        }

        if (data == null)
        {
            data = await DownloadAsync(url, ct);
            if (data == null) return null;

            lock (_lruLock)
                PutToRamCacheUnsafe(url, data);

            try
            {
                var diskPath = GetDiskCachePath(url, ext);
                await File.WriteAllBytesAsync(diskPath, data, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthImageLoader] Disk write failed: {ex.Message}");
            }
        }

        return data;
    }

    #region RAM Cache

    private byte[]? GetFromRamCacheUnsafe(string url)
    {
        if (!_lruMap.TryGetValue(url, out var node)) return null;
        _lruList.Remove(node);
        _lruList.AddFirst(node);
        return node.Value.Data;
    }

    private void PutToRamCacheUnsafe(string url, byte[] data)
    {
        if (data.Length >= LohThresholdBytes)
        {
            MemoryDiagnostics.OnImageLargeSkipped();
            return;
        }

        if (_lruMap.TryGetValue(url, out var existing))
        {
            _ramCacheBytes -= existing.Value.Data.Length;
            MemoryDiagnostics.OnImageRamCacheEvict(existing.Value.Data.Length);
            _lruList.Remove(existing);
            _lruMap.Remove(url);
        }

        _lruMap[url] = _lruList.AddFirst((url, data));
        _ramCacheBytes += data.Length;
        MemoryDiagnostics.OnImageRamCachePut(data.Length);

        while ((_lruList.Count > MaxRamCacheItems || _ramCacheBytes > MaxRamCacheBytes) && _lruList.Last != null)
        {
            var last = _lruList.Last!;
            _ramCacheBytes -= last.Value.Data.Length;
            MemoryDiagnostics.OnImageRamCacheEvict(last.Value.Data.Length);
            _lruMap.Remove(last.Value.Key);
            _lruList.RemoveLast();
        }
    }

    #endregion

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (url.StartsWith(_apiBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                var token = _sessionStore.Token;
                if (string.IsNullOrEmpty(token))
                {
                    Debug.WriteLine($"[AuthImageLoader] No token for: {GetFileName(url)}");
                    return null;
                }
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[AuthImageLoader] {(int)response.StatusCode} for: {GetFileName(url)}");
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!string.IsNullOrEmpty(contentType) &&
                !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[AuthImageLoader] Non-image '{contentType}': {GetFileName(url)}");
                return null;
            }

            MemoryDiagnostics.OnImageNetworkFetch();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[AuthImageLoader] HTTP error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthImageLoader] Error: {ex.Message}");
            return null;
        }
    }

    public void ClearCache()
    {
        lock (_lruLock)
        {
            MemoryDiagnostics.OnImageCacheCleared(_lruMap.Count, _ramCacheBytes);
            Debug.WriteLine($"[AuthImageLoader] Clear: count={_lruMap.Count}, ram={_ramCacheBytes / 1024 / 1024}MB");

            _lruList.Clear();
            _lruMap.Clear();
            _ramCacheBytes = 0;

            foreach (var (_, (task, cts)) in _inflight)
            {
                try
                {
                    if (!task.IsCompleted)
                        cts.Cancel();
                    // Не диспожим — LoadBytesWithCtsAsync сделает это в finally
                }
                catch { }
            }
            _inflight.Clear();
        }
    }

    public void InvalidateUrl(string url)
    {
        lock (_lruLock)
        {
            if (_lruMap.TryGetValue(url, out var node))
            {
                _ramCacheBytes -= node.Value.Data.Length;
                _lruList.Remove(node);
                _lruMap.Remove(url);
            }
        }

        try
        {
            var urlWithoutQuery = url.Contains('?') ? url[..url.IndexOf('?')] : url;
            var ext = GetExtension(urlWithoutQuery);
            var diskPath = GetDiskCachePath(urlWithoutQuery, ext);
            if (File.Exists(diskPath))
                File.Delete(diskPath);

            var diskPathFull = GetDiskCachePath(url, GetExtension(url));
            if (File.Exists(diskPathFull))
                File.Delete(diskPathFull);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthImageLoader] InvalidateUrl disk delete failed: {ex.Message}");
        }
    }

    public void InvalidateByRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        var normalized = relativePath.TrimStart('/').ToLowerInvariant();
        Debug.WriteLine($"[AuthImageLoader] InvalidateByRelativePath: '{normalized}'");
        Debug.WriteLine($"[AuthImageLoader] Cache keys count: {_lruMap.Count}");

        List<string> toRemove;
        lock (_lruLock)
        {
            toRemove = [];
            foreach (var key in _lruMap.Keys)
            {
                Debug.WriteLine($"[AuthImageLoader] Cache key: '{key}'");
                if (key.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    toRemove.Add(key);
            }

            foreach (var key in toRemove)
            {
                if (_lruMap.TryGetValue(key, out var node))
                {
                    _ramCacheBytes -= node.Value.Data.Length;
                    _lruList.Remove(node);
                    _lruMap.Remove(key);
                }
            }
        }

        foreach (var key in toRemove)
        {
            try
            {
                var ext = GetExtension(key);
                var diskPath = GetDiskCachePath(key, ext);
                if (File.Exists(diskPath))
                    File.Delete(diskPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthImageLoader] Disk delete failed for {key}: {ex.Message}");
            }
        }

        Debug.WriteLine($"[AuthImageLoader] Invalidated {toRemove.Count} entries for '{relativePath}'");
    }

    #region Helpers

    private string GetDiskCachePath(string url, string ext)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        if (string.IsNullOrEmpty(ext)) ext = ".img";
        return Path.Combine(_cacheDirectory, $"{hash}{ext}");
    }

    private static string GetExtension(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var dot = path.LastIndexOf('.');
            if (dot < 0) return "";
            var ext = path[dot..];
            var q = ext.IndexOf('?');
            return q >= 0 ? ext[..q] : ext;
        }
        catch { return ""; }
    }

    private static string GetFileName(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var i = path.LastIndexOf('/');
            return i >= 0 ? path[(i + 1)..] : path;
        }
        catch { return url; }
    }

    #endregion

    public bool IsCached(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        lock (_lruLock)
        {
            return _lruMap.ContainsKey(url) || (url.Contains('?') && _lruMap.ContainsKey(url[..url.IndexOf('?')]))
                || (!url.Contains('?') && _lruMap.Keys.Any(k => k.StartsWith(url + "?", StringComparison.OrdinalIgnoreCase)));
        }
    }

    public void Dispose() => ClearCache();
}