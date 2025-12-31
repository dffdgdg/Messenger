using MessengerDesktop.Services.Auth;
using MessengerShared.Response;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Api
{
    public interface IApiClientService : IDisposable
    {
        Task<ApiResponse<T>> GetAsync<T>(string url, CancellationToken ct = default);
        Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default);
        Task<ApiResponse<T>> PostAsync<T>(string url, object data, CancellationToken ct = default);
        Task<ApiResponse> PostAsync(string url, object? data, CancellationToken ct = default);
        Task<ApiResponse<T>> PutAsync<T>(string url, object data, CancellationToken ct = default);
        Task<ApiResponse<TResponse>> PutAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default);
        Task<ApiResponse> PutAsync(string url, object data, CancellationToken ct = default);
        Task<ApiResponse> DeleteAsync(string url, CancellationToken ct = default);
        Task<ApiResponse<T>> UploadFileAsync<T>(string url, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
        Task<Stream?> GetStreamAsync(string url, CancellationToken ct = default);
    }

    public class ApiClientService : IApiClientService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ISessionStore _sessionStore;
        private bool _disposed;

        private const long LargeFileThreshold = 10 * 1024 * 1024; // 10MB

        public ApiClientService(HttpClient httpClient, ISessionStore sessionStore)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            UpdateAuthorizationHeader();

            _sessionStore.SessionChanged += OnSessionChanged;

            if (_sessionStore is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged += OnSessionPropertyChanged;
            }
        }

        private void OnSessionChanged() => UpdateAuthorizationHeader();

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ISessionStore.Token)
                or nameof(ISessionStore.IsAuthenticated))
            {
                UpdateAuthorizationHeader();
            }
        }

        private void UpdateAuthorizationHeader()
        {
            try
            {
                if (!string.IsNullOrEmpty(_sessionStore.Token))
                {
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", _sessionStore.Token);
                }
                else
                {
                    _httpClient.DefaultRequestHeaders.Authorization = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating authorization header: {ex.Message}");
            }
        }

        public async Task<ApiResponse> DeleteAsync(string url, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                var response = await _httpClient.DeleteAsync(url, ct);
                return await ProcessResponseAsync(response, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(ex);
            }
        }
        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            var token = _sessionStore.Token;
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
            return request;
        }

        public async Task<ApiResponse<T>> GetAsync<T>(string url, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            try
            {
                using var request = CreateRequest(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request, ct);
                return await ProcessResponseAsync<T>(response, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return CreateErrorResponse<T>(ex); }
        }

        public async Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, data, _jsonOptions, ct);
                return await ProcessResponseAsync<TResponse>(response, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorResponse<TResponse>(ex);
            }
        }

        public async Task<ApiResponse<T>> PostAsync<T>(string url, object data, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, data, _jsonOptions, ct);
                return await ProcessResponseAsync<T>(response, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorResponse<T>(ex);
            }
        }

        public async Task<ApiResponse> PostAsync(string url, object? data, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, data, _jsonOptions, ct);
                return await ProcessResponseAsync(response, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(ex);
            }
        }

        public async Task<ApiResponse<T>> PutAsync<T>(string url, object data, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                var response = await _httpClient.PutAsJsonAsync(url, data, _jsonOptions, ct);
                return await ProcessResponseAsync<T>(response, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorResponse<T>(ex);
            }
        }

        public async Task<ApiResponse<TResponse>> PutAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                var response = await _httpClient.PutAsJsonAsync(url, data, _jsonOptions, ct);
                return await ProcessResponseAsync<TResponse>(response, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorResponse<TResponse>(ex);
            }
        }

        public async Task<ApiResponse> PutAsync(string url, object data, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                var response = await _httpClient.PutAsJsonAsync(url, data, _jsonOptions, ct);
                return await ProcessResponseAsync(response, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(ex);
            }
        }

        public async Task<Stream?> GetStreamAsync(string url, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (!string.IsNullOrEmpty(_sessionStore.Token))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", _sessionStore.Token);
                }

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"GetStreamAsync failed: {response.StatusCode} for {url}");
                    response.Dispose();
                    return null;
                }

                var contentLength = response.Content.Headers.ContentLength;

                if (contentLength > LargeFileThreshold)
                {
                    return await CreateTempFileStreamAsync(response, ct);
                }

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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStreamAsync error for {url}: {ex}");
                return null;
            }
        }

        private static async Task<Stream> CreateTempFileStreamAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var tempPath = Path.GetTempFileName();

            try
            {
                await using (var fileStream = new FileStream(tempPath, FileMode.Create,
                    FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await response.Content.CopyToAsync(fileStream, ct);
                }

                response.Dispose();

                return new FileStream(tempPath, FileMode.Open, FileAccess.Read,FileShare.Read, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            }
            catch
            {
                response.Dispose();
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }

        public async Task<ApiResponse<T>> UploadFileAsync<T>(string url, Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            try
            {
                using var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                content.Add(streamContent, "file", fileName);

                var response = await _httpClient.PostAsync(url, content, ct);
                return await ProcessResponseAsync<T>(response, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorResponse<T>(ex);
            }
        }

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
                    Timestamp = DateTime.Now
                };
            }

            try
            {
                var apiResponse = JsonSerializer.Deserialize<ApiResponse<T>>(json, _jsonOptions);
                if (apiResponse != null)
                    return apiResponse;

                var directData = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                return new ApiResponse<T>
                {
                    Success = true,
                    Data = directData,
                    Timestamp = DateTime.Now
                };
            }
            catch (JsonException ex)
            {
                return new ApiResponse<T>
                {
                    Success = false,
                    Error = $"Deserialization error: {ex.Message}",
                    Details = json,
                    Timestamp = DateTime.Now
                };
            }
        }

        private async Task<ApiResponse> ProcessResponseAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ApiResponse
                {
                    Success = false,
                    Error = $"HTTP {response.StatusCode}",
                    Details = json,
                    Timestamp = DateTime.Now
                };
            }

            try
            {
                var apiResponse = JsonSerializer.Deserialize<ApiResponse>(json, _jsonOptions);
                return apiResponse ?? new ApiResponse { Success = true, Timestamp = DateTime.Now };
            }
            catch (JsonException ex)
            {
                return new ApiResponse
                {
                    Success = false,
                    Error = $"Deserialization error: {ex.Message}",
                    Details = json,
                    Timestamp = DateTime.Now
                };
            }
        }

        private static ApiResponse<T> CreateErrorResponse<T>(Exception ex) => new()
        {
            Success = false,
            Error = ex.Message,
            Timestamp = DateTime.Now
        };

        private static ApiResponse CreateErrorResponse(Exception ex) => new()
        {
            Success = false,
            Error = ex.Message,
            Timestamp = DateTime.Now
        };

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(ApiClientService));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _sessionStore.SessionChanged -= OnSessionChanged;

            if (_sessionStore is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged -= OnSessionPropertyChanged;
            }

            GC.SuppressFinalize(this);
        }
    }
}