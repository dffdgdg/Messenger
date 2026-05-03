using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop.Infrastructure.Media;

public interface IImageCacheService
{
    //Task<Bitmap?> GetAsync(string? url, CancellationToken ct = default);
    void Evict(string url);
    void Clear();
}

public sealed class ImageCacheService(HttpClient http, ISessionStore session, string apiBase) : IImageCacheService, IDisposable
{
    private const int MaxEntries = 80;
    private const int EvictBatchSize = 20;
    private const long MaxBitmapSizeBytes = 4L * 1024 * 1024;

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly ISessionStore _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly string _apiBase = apiBase.TrimEnd('/');

    private readonly Dictionary<string, CacheEntry> _cache = [];
    private readonly Lock _lock = new();

    private readonly Dictionary<string, Task<Bitmap?>> _inflight = [];
    private readonly Lock _inflightLock = new();

    private sealed record CacheEntry(Bitmap Bitmap, long SizeBytes)
    {
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    }

    //public async Task<Bitmap?> GetAsync(string? url, CancellationToken ct = default)
    //{
    //    if (string.IsNullOrWhiteSpace(url)) return null;

    //    var resolved = Resolve(url);

    //    lock (_lock)
    //    {
    //        if (_cache.TryGetValue(resolved, out var hit))
    //        {
    //            hit.LastAccess = DateTime.UtcNow;
    //            return hit.Bitmap;
    //        }
    //    }

    //    Task<Bitmap?> task;
    //    bool isOwner;

    //    lock (_inflightLock)
    //    {
    //        if (_inflight.TryGetValue(resolved, out task!))
    //        {
    //            isOwner = false;
    //        }
    //        else
    //        {
    //            task = LoadAsync(resolved, ct);
    //            _inflight[resolved] = task;
    //            isOwner = true;
    //        }
    //    }

    //    try
    //    {
    //        return await task;
    //    }
    //    finally
    //    {
    //        if (isOwner)
    //        {
    //            lock (_inflightLock)
    //            {
    //                _inflight.Remove(resolved);
    //            }
    //        }
    //    }
    //}

    //private async Task<Bitmap?> LoadAsync(string url, CancellationToken ct)
    //{
    //    try
    //    {
    //        using var req = new HttpRequestMessage(HttpMethod.Get, url);

    //        var token = _session.Token;
    //        if (!string.IsNullOrEmpty(token))
    //            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    //        using var resp = await _http.SendAsync(
    //            req, HttpCompletionOption.ResponseHeadersRead, ct);

    //        if (!resp.IsSuccessStatusCode) return null;

    //        await using var networkStream = await resp.Content.ReadAsStreamAsync(ct);
    //        await using var ms = new MemoryStream();
    //        await networkStream.CopyToAsync(ms, ct);
    //        ms.Position = 0;

    //        ct.ThrowIfCancellationRequested();

    //        var bitmap = new Bitmap(ms);
    //        var size = EstimateBytes(bitmap);

    //        if (size <= MaxBitmapSizeBytes)
    //        {
    //            lock (_lock)
    //            {
    //                _cache[url] = new CacheEntry(bitmap, size);
    //                EvictIfNeeded();
    //            }
    //        }

    //        return bitmap;
    //    }
    //    catch (OperationCanceledException) { return null; }
    //    catch (Exception ex)
    //    {
    //        Debug.WriteLine($"[ImageCache] Load failed {url}: {ex.Message}");
    //        return null;
    //    }
    //}

    public void Evict(string url)
    {
        var resolved = Resolve(url);
        lock (_lock)
        {
            if (_cache.Remove(resolved, out var entry))
                entry.Bitmap.Dispose();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var e in _cache.Values)
                try { e.Bitmap.Dispose(); } catch { }
            _cache.Clear();
        }
        Debug.WriteLine("[ImageCache] Cleared");
    }

    private void EvictIfNeeded()
    {
        if (_cache.Count <= MaxEntries) return;

        var toRemove = _cache
            .OrderBy(kv => kv.Value.LastAccess)
            .Take(EvictBatchSize)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            if (_cache.Remove(key, out var entry))
                try { entry.Bitmap.Dispose(); } catch { }
        }

        Debug.WriteLine($"[ImageCache] Evicted {toRemove.Count}, remaining: {_cache.Count}");
    }

    private string Resolve(string url)
    {
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("avares://", StringComparison.Ordinal))
        {
            return url;
        }

        return $"{_apiBase}/{url.TrimStart('/')}";
    }

    private static long EstimateBytes(Bitmap b) =>
        (long)b.Size.Width * (long)b.Size.Height * 4;

    public void Dispose() => Clear();
}