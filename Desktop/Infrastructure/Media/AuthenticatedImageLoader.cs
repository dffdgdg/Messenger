using Avalonia.Media.Imaging;
using Desktop.Infrastructure.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop.Infrastructure.Media;

public sealed class AuthenticatedImageLoader : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ISessionStore _sessionStore;
    private readonly string _apiBaseUrl;
    private readonly string _cacheDirectory;

    // Кэш сырых байтов (LRU)
    private readonly LinkedList<(string Key, byte[] Data)> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, byte[] Data)>> _lruMap = [];
    private readonly Lock _lruLock = new();
    private const int MaxRamCacheItems = 80;
    private long _ramCacheBytes;
    private const long MaxRamCacheBytes = 30L * 1024 * 1024; // 30 MB
    private const long LohThresholdBytes = 85 * 1024; // 85 KB — порог LOH ←

    // Кэш выполняющихся задач, чтобы не качать одно и то же
    private readonly Dictionary<string, Task<byte[]?>> _inflight = [];

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

        // ← Регистрируем в диагностике
        MemoryDiagnostics.RegisterImageLoader(
            getStats: () => { lock (_lruLock) return (_lruMap.Count, _ramCacheBytes); },
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
            if (_inflight.TryGetValue(url, out var existingTask))
            {
                loadTask = existingTask;
                isOwner = false;
            }
            else
            {
                loadTask = LoadBytesAsync(url, ext, ct);
                _inflight[url] = loadTask;
                isOwner = true;
            }
        }

        try
        {
            return await loadTask;
        }
        finally
        {
            if (isOwner)
            {
                lock (_lruLock)
                    _inflight.Remove(url);
            }
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
                    MemoryDiagnostics.OnImageDiskHit(); // ← счётчик
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

    #region RAM Cache (только byte[])

    private byte[]? GetFromRamCacheUnsafe(string url)
    {
        if (!_lruMap.TryGetValue(url, out var node)) return null;
        _lruList.Remove(node);
        _lruList.AddFirst(node);
        return node.Value.Data;
    }

    private void PutToRamCacheUnsafe(string url, byte[] data)
    {
        // ← Не кэшируем большие массивы в RAM (LOH), только на диск
        if (data.Length >= LohThresholdBytes)
        {
            MemoryDiagnostics.OnImageLargeSkipped();
            return;
        }

        if (_lruMap.TryGetValue(url, out var existing))
        {
            _ramCacheBytes -= existing.Value.Data.Length;
            MemoryDiagnostics.OnImageRamCacheEvict(existing.Value.Data.Length); // ←
            _lruList.Remove(existing);
            _lruMap.Remove(url);
        }

        var node = _lruList.AddFirst((url, data));
        _lruMap[url] = node;
        _ramCacheBytes += data.Length;
        MemoryDiagnostics.OnImageRamCachePut(data.Length); // ←

        while ((_lruList.Count > MaxRamCacheItems ||
                _ramCacheBytes > MaxRamCacheBytes)
               && _lruList.Last != null)
        {
            var last = _lruList.Last!;
            _ramCacheBytes -= last.Value.Data.Length;
            MemoryDiagnostics.OnImageRamCacheEvict(last.Value.Data.Length); // ←
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
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

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

            MemoryDiagnostics.OnImageNetworkFetch(); // ← счётчик
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
            MemoryDiagnostics.OnImageCacheCleared(_lruMap.Count, _ramCacheBytes); // ← до очистки
            Debug.WriteLine(
                $"[AuthImageLoader] Clear: bytes={_lruMap.Count}, " +
                $"ram={_ramCacheBytes / 1024 / 1024}MB");

            _lruList.Clear();
            _lruMap.Clear();
            _ramCacheBytes = 0;
            _inflight.Clear();
        }
    }

    #region Helpers

    private string GetDiskCachePath(string url, string ext)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
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

    public void Dispose() => ClearCache();
}