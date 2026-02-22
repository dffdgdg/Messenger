using AsyncImageLoader.Loaders;
using MessengerDesktop.Services.Auth;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace MessengerDesktop.Infrastructure.ImageLoading;

public sealed class AuthenticatedImageLoader(
    HttpClient httpClient,
    ISessionStore sessionStore,
    string apiBaseUrl) : RamCachedWebImageLoader
{
    private readonly HttpClient _httpClient = httpClient
        ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ISessionStore _sessionStore = sessionStore
        ?? throw new ArgumentNullException(nameof(sessionStore));
    private readonly string _apiBaseUrl = apiBaseUrl?.TrimEnd('/')
        ?? throw new ArgumentNullException(nameof(apiBaseUrl));

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".avif"
    };

    protected override async Task<byte[]?> LoadDataFromExternalAsync(string url)
    {
        var ext = GetExtension(url);
        if (!string.IsNullOrEmpty(ext) && !ImageExtensions.Contains(ext))
        {
            Debug.WriteLine($"[AuthImageLoader] Skipping non-image: {GetFileName(url)}");
            return null;
        }

        if (!url.StartsWith(_apiBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return await base.LoadDataFromExternalAsync(url);
        }

        var token = _sessionStore.Token;
        if (string.IsNullOrEmpty(token))
        {
            Debug.WriteLine($"[AuthImageLoader] No token, skipping: {GetFileName(url)}");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient
                .SendAsync(request)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine(
                    $"[AuthImageLoader] {(int)response.StatusCode} " +
                    $"for: {GetFileName(url)}");
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!string.IsNullOrEmpty(contentType)
                && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine(
                    $"[AuthImageLoader] Non-image content-type '{contentType}' " +
                    $"for: {GetFileName(url)}");
                return null;
            }

            return await response.Content
                .ReadAsByteArrayAsync()
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine(
                $"[AuthImageLoader] HTTP error for {GetFileName(url)}: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[AuthImageLoader] Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts file extension from URL.
    /// "https://localhost:7190/uploads/chats/4/abc.wav?v=123" → ".wav"
    /// </summary>
    private static string GetExtension(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var dot = path.LastIndexOf('.');
            if (dot < 0) return "";
            var ext = path[dot..];
            // Отсекаем query-параметры, если они попали
            var q = ext.IndexOf('?');
            return q >= 0 ? ext[..q] : ext;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Extracts just the file name from URL for readable logs.
    /// "https://localhost:7190/avatars/users/abc.webp?v=123" → "abc.webp"
    /// </summary>
    private static string GetFileName(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        }
        catch
        {
            return url;
        }
    }
}