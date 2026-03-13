using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Api;

public interface IApiClientService : IDisposable
{
    Task<ApiResponse<T>> GetAsync<T>(string url, CancellationToken ct = default);
    Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default);
    Task<ApiResponse<T>> PostAsync<T>(string url, object data, CancellationToken ct = default);
    Task<ApiResponse<object>> PostAsync(string url, object? data, CancellationToken ct = default);
    Task<ApiResponse<T>> PutAsync<T>(string url, object data, CancellationToken ct = default);
    Task<ApiResponse<TResponse>> PutAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default);
    Task<ApiResponse<object>> PutAsync(string url, object data, CancellationToken ct = default);
    Task<ApiResponse<object>> DeleteAsync(string url, CancellationToken ct = default);
    Task<ApiResponse<T>> UploadFileAsync<T>(string url, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream?> GetStreamAsync(string url, CancellationToken ct = default);
}

public sealed class ApiClientService : IApiClientService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ISessionStore _sessionStore;
    private readonly IAuthManager _authManager;
    private bool _disposed;

    private const long LargeFileThreshold = 10 * 1024 * 1024;

    private static readonly string[] NoRefreshUrls =
    [
        ApiEndpoints.Auth.Login,
        ApiEndpoints.Auth.Refresh,
        ApiEndpoints.Auth.Revoke
    ];

    public ApiClientService(HttpClient httpClient, ISessionStore sessionStore, IAuthManager authManager)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        AttachAuth(request);
        return request;
    }

    private void AttachAuth(HttpRequestMessage request)
    {
        var token = _sessionStore.Token;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string url, T data)
    {
        var request = CreateRequest(method, url);
        request.Content = JsonContent.Create(data, options: _jsonOptions);
        return request;
    }

    private static bool ShouldAttemptRefresh(string url)
    {
        foreach (var noRefreshUrl in NoRefreshUrls)
        {
            if (url.Contains(noRefreshUrl, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private async Task<HttpResponseMessage> SendWithRefreshAsync(Func<Task<HttpResponseMessage>> createAndSendRequest, string url)
    {
        var response = await createAndSendRequest();

        if (response.StatusCode != HttpStatusCode.Unauthorized || !ShouldAttemptRefresh(url))
            return response;

        Debug.WriteLine($"[ApiClient] Got 401 for {url}, attempting token refresh...");

        response.Dispose();

        var refreshed = await _authManager.TryRefreshTokenAsync();

        if (refreshed)
        {
            Debug.WriteLine("[ApiClient] Token refreshed, retrying...");
            return await createAndSendRequest();
        }

        Debug.WriteLine("[ApiClient] Token refresh failed");
        return new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new ApiResponse<object>
                {
                    Success = false,
                    Error = "Сессия истекла. Войдите заново.",
                    Timestamp = DateTime.UtcNow
                }, _jsonOptions),
                Encoding.UTF8,
                "application/json")
        };
    }

    public async Task<ApiResponse<T>> GetAsync<T>(string url, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateRequest(HttpMethod.Get, url);
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync<T>(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse<T>(ex); }
    }

    public async Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string url, TRequest data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateJsonRequest(HttpMethod.Post, url, data);
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync<TResponse>(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse<TResponse>(ex); }
    }

    public async Task<ApiResponse<T>> PostAsync<T>(string url, object data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateJsonRequest(HttpMethod.Post, url, data);
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync<T>(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse<T>(ex); }
    }

    public async Task<ApiResponse<object>> PostAsync(string url, object? data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateJsonRequest(HttpMethod.Post, url, data);
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse(ex); }
    }

    public async Task<ApiResponse<T>> PutAsync<T>(string url, object data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateJsonRequest(HttpMethod.Put, url, data);
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync<T>(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse<T>(ex); }
    }

    public async Task<ApiResponse<TResponse>> PutAsync<TRequest, TResponse>(
        string url, TRequest data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateJsonRequest(HttpMethod.Put, url, data);
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync<TResponse>(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse<TResponse>(ex); }
    }

    public async Task<ApiResponse<object>> PutAsync(string url, object data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateJsonRequest(HttpMethod.Put, url, data);
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse(ex); }
    }

    public async Task<ApiResponse<object>> DeleteAsync(string url, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateRequest(HttpMethod.Delete, url);
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse(ex); }
    }

    public async Task<Stream?> GetStreamAsync(string url, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var response = await SendWithRefreshAsync(
                () =>
                {
                    var request = CreateRequest(HttpMethod.Get, url);
                    return _httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                url);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"GetStreamAsync failed: {response.StatusCode} for {url}");
                response.Dispose();
                return null;
            }

            var contentLength = response.Content.Headers.ContentLength;

            if (contentLength > LargeFileThreshold)
                return await CreateTempFileStreamAsync(response, ct);

            var memoryStream = new MemoryStream();
            try
            {
                await response.Content.CopyToAsync(memoryStream, ct);
                memoryStream.Position = 0;
                response.Dispose();
                return memoryStream;
            }
            catch
            {
                await memoryStream.DisposeAsync();
                response.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetStreamAsync ошибка для {url}: {ex}");
            return null;
        }
    }

    public async Task<ApiResponse<T>> UploadFileAsync<T>(
        string url, Stream fileStream, string fileName, string contentType,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            var startPosition = fileStream.CanSeek ? fileStream.Position : -1;

            var response = await SendWithRefreshAsync(
                () =>
                {
                    if (startPosition >= 0 && fileStream.Position != startPosition)
                        fileStream.Position = startPosition;

                    var content = new MultipartFormDataContent();
                    var streamContent = new StreamContent(fileStream);
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    content.Add(streamContent, "file", fileName);

                    var request = CreateRequest(HttpMethod.Post, url);
                    request.Content = content;
                    return _httpClient.SendAsync(request, ct);
                },
                url);

            return await ProcessResponseAsync<T>(response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CreateErrorResponse<T>(ex); }
    }

    #region Response Processing

    private async Task<ApiResponse<T>> ProcessResponseAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Error = $"HTTP {response.StatusCode}",
                Details = json,
                Timestamp = DateTime.UtcNow
            };
        }

        try
        {
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<T>>(json, _jsonOptions);
            if (apiResponse != null) return apiResponse;

            var directData = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            return new ApiResponse<T>
            {
                Success = true,
                Data = directData,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (JsonException ex)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Error = $"Ошибка десериализации: {ex.Message}",
                Details = json,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    private async Task<ApiResponse<object>> ProcessResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return new ApiResponse<object>
            {
                Success = false,
                Error = $"HTTP {response.StatusCode}",
                Details = json,
                Timestamp = DateTime.UtcNow
            };
        }

        try
        {
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<object>>(json, _jsonOptions);
            return apiResponse ?? new ApiResponse<object>
            {
                Success = true,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (JsonException ex)
        {
            return new ApiResponse<object>
            {
                Success = false,
                Error = $"Ошибка десериализации: {ex.Message}",
                Details = json,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    #endregion

    #region Helpers

    private static async Task<Stream> CreateTempFileStreamAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await response.Content.CopyToAsync(fileStream, ct);
            }

            response.Dispose();

            return new FileStream(tempPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }
        catch
        {
            response.Dispose();
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* ignore */ }
            }
            throw;
        }
    }

    private static ApiResponse<T> CreateErrorResponse<T>(Exception ex) => new()
    {
        Success = false,
        Error = ex.Message,
        Timestamp = DateTime.UtcNow
    };

    private static ApiResponse<object> CreateErrorResponse(Exception ex) => new()
    {
        Success = false,
        Error = ex.Message,
        Timestamp = DateTime.UtcNow
    };

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, nameof(ApiClientService));

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}