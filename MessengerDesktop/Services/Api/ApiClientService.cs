using MessengerDesktop.Services.Auth;
using MessengerShared.Response;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
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
        private readonly IAuthService _authService;
        private bool _disposed;

        public ApiClientService(HttpClient httpClient, IAuthService authService)  
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            SafeUpdateAuthorizationHeader();

            if (_authService is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged += OnAuthServicePropertyChanged;
            }
        }

        private void OnAuthServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AuthService.Token) or nameof(AuthService.IsAuthenticated))
            {
                SafeUpdateAuthorizationHeader();
            }
        }

        private void SafeUpdateAuthorizationHeader()
        {
            try
            {
                if (!string.IsNullOrEmpty(_authService.Token))
                {
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authService.Token);
                }
                else
                {
                    _httpClient.DefaultRequestHeaders.Authorization = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating authorization header: {ex.Message}");
            }
        }

        public async Task<ApiResponse> DeleteAsync(string url, CancellationToken ct = default)
        {
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

        public async Task<ApiResponse<T>> GetAsync<T>(string url, CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(url, ct);
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

        public async Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default)
        {
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
            try
            {
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"GetStreamAsync failed: {response.StatusCode}");
                    return null;
                }

                await using var networkStream = await response.Content.ReadAsStreamAsync(ct);
                var memoryStream = new MemoryStream();
                await networkStream.CopyToAsync(memoryStream, ct);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetStreamAsync error: {ex}");
                return null;
            }
        }

        public async Task<ApiResponse<T>> UploadFileAsync<T>(string url, Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_authService is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged -= OnAuthServicePropertyChanged;
            }

            GC.SuppressFinalize(this);
        }

    }
}