using AsyncImageLoader.Loaders;
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

namespace MessengerDesktop.Infrastructure;

public sealed class AuthenticatedImageLoader : BaseWebImageLoader
{
    private readonly HttpClient _httpClient;
    private readonly ISessionStore _sessionStore;
    private readonly string _apiBaseUrl;
    private readonly string _cacheDirectory;

    private readonly LinkedList<(string Key, byte[] Data)> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, byte[] Data)>> _lruMap = [];
    private readonly Lock _lruLock = new();
    private const int MaxRamCacheItems = 50;
    private long _ramCacheBytes;
    private const long MaxRamCacheBytes = 30L * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".avif" };

    public AuthenticatedImageLoader(HttpClient httpClient, ISessionStore sessionStore, string apiBaseUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _apiBaseUrl = apiBaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(apiBaseUrl));

        _cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MessengerDesktop", "ImageCache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    protected override async Task<byte[]?> LoadDataFromExternalAsync(string url)
    {
        var ext = GetExtension(url);
        if (!string.IsNullOrEmpty(ext) && !ImageExtensions.Contains(ext))
        {
            Debug.WriteLine($"[AuthImageLoader] Skipping non-image: {GetFileName(url)}");
            return null;
        }

        var cached = GetFromRamCache(url);
        if (cached != null) return cached;

        var diskPath = GetDiskCachePath(url, ext);
        if (File.Exists(diskPath))
        {
            try
            {
                var diskData = await File.ReadAllBytesAsync(diskPath).ConfigureAwait(false);
                PutToRamCache(url, diskData);
                return diskData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthImageLoader] Disk read failed: {ex.Message}");
            }
        }

        var downloaded = await DownloadAsync(url).ConfigureAwait(false);
        if (downloaded == null) return null;

        try
        {
            await File.WriteAllBytesAsync(diskPath, downloaded).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthImageLoader] Disk write failed: {ex.Message}");
        }

        PutToRamCache(url, downloaded);
        return downloaded;
    }

    private async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            if (!url.StartsWith(_apiBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                    : null;
            }

            var token = _sessionStore.Token;
            if (string.IsNullOrEmpty(token))
            {
                Debug.WriteLine($"[AuthImageLoader] No token, skipping: {GetFileName(url)}");
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var authResponse = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!authResponse.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[AuthImageLoader] {(int)authResponse.StatusCode} for: {GetFileName(url)}");
                return null;
            }

            var contentType = authResponse.Content.Headers.ContentType?.MediaType ?? "";
            if (!string.IsNullOrEmpty(contentType) &&
                !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[AuthImageLoader] Non-image content-type '{contentType}' for: {GetFileName(url)}");
                return null;
            }

            return await authResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[AuthImageLoader] HTTP error for {GetFileName(url)}: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthImageLoader] Error: {ex.Message}");
            return null;
        }
    }

    private byte[]? GetFromRamCache(string url)
    {
        lock (_lruLock)
        {
            if (!_lruMap.TryGetValue(url, out var node)) return null;
            _lruList.Remove(node);
            _lruList.AddFirst(node);
            return node.Value.Data;
        }
    }

    private void PutToRamCache(string url, byte[] data)
    {
        lock (_lruLock)
        {
            if (_lruMap.TryGetValue(url, out var existing))
            {
                _ramCacheBytes -= existing.Value.Data.Length;
                _lruList.Remove(existing);
                _lruMap.Remove(url);
            }

            var node = _lruList.AddFirst((url, data));
            _lruMap[url] = node;
            _ramCacheBytes += data.Length;

            while ((_lruList.Count > MaxRamCacheItems || _ramCacheBytes > MaxRamCacheBytes)
                   && _lruList.Last != null)
            {
                var last = _lruList.Last!;
                _ramCacheBytes -= last.Value.Data.Length;
                _lruMap.Remove(last.Value.Key);
                _lruList.RemoveLast();
            }
        }
    }

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
    public void ClearCache()
    {
        lock (_lruLock)
        {
            Debug.WriteLine($"[AuthImageLoader] Clearing RAM cache: {_lruMap.Count} items, {_ramCacheBytes / 1024 / 1024} MB");
            _lruList.Clear();
            _lruMap.Clear();
            _ramCacheBytes = 0;
        }
    }

    private static string GetFileName(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        }
        catch { return url; }
    }
}