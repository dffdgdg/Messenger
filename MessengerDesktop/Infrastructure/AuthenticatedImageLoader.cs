using AsyncImageLoader.Loaders;
using MessengerDesktop.Services.Auth;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace MessengerDesktop.Infrastructure.ImageLoading;

public sealed class AuthenticatedImageLoader(HttpClient httpClient,ISessionStore sessionStore, string apiBaseUrl) : RamCachedWebImageLoader
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ISessionStore _sessionStore = sessionStore
            ?? throw new ArgumentNullException(nameof(sessionStore));
    private readonly string _apiBaseUrl = apiBaseUrl?.TrimEnd('/')
            ?? throw new ArgumentNullException(nameof(apiBaseUrl));

    protected override async Task<byte[]?> LoadDataFromExternalAsync(string url)
    {
        // External URLs (CDN, gravatar, etc.) — use base loader
        if (!url.StartsWith(_apiBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return await base.LoadDataFromExternalAsync(url);
        }

        // Our API — must attach token
        var token = _sessionStore.Token;
        if (string.IsNullOrEmpty(token))
        {
            Debug.WriteLine($"[AuthImageLoader] No token, skipping: {GetFileName(url)}");
            return null;
        }

        try
        {
            // Create a NEW request each time (headers are per-request).
            // We do NOT set DefaultRequestHeaders on the shared HttpClient
            // because ApiClientService already manages that,
            // and concurrent requests could race.
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
            // Timeout — suppress log spam
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
    /// Extracts just the file name from URL for readable logs.
    /// "https://localhost:7190/avatars/users/abc.webp?v=123"
    /// → "abc.webp"
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