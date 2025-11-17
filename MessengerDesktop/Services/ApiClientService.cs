using MessengerShared.Response;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    public interface IApiClientService
    {
        Task<ApiResponse<T>> GetAsync<T>(string url);
        Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest data);
        Task<ApiResponse<T>> PostAsync<T>(string url, object data);
        Task<ApiResponse> PostAsync(string url, object data);
        Task<ApiResponse<T>> PutAsync<T>(string url, object data);
        Task<ApiResponse<TResponse>> PutAsync<TRequest, TResponse>(string url, TRequest data);
        Task<ApiResponse> PutAsync(string url, object data);
        Task<ApiResponse> DeleteAsync(string url);
        Task<ApiResponse<T>> UploadFileAsync<T>(string url, Stream fileStream, string fileName, string contentType);
        Task<Stream?> GetStreamAsync(string url);
    }

    public class ApiClientService : IApiClientService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly AuthService _authService;

        public ApiClientService(HttpClient httpClient, AuthService authService)
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

            _authService.PropertyChanged += OnAuthServicePropertyChanged;
        }

        private void OnAuthServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AuthService.Token) || e.PropertyName == nameof(AuthService.IsAuthenticated))
                SafeUpdateAuthorizationHeader();
        }

        private void SafeUpdateAuthorizationHeader()
        {
            try
            {
                if (!string.IsNullOrEmpty(_authService.Token))
                {
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authService.Token);
                    System.Diagnostics.Debug.WriteLine($"Authorization header set for HttpClient");
                }
                else
                {
                    _httpClient.DefaultRequestHeaders.Authorization = null;
                    System.Diagnostics.Debug.WriteLine($"Authorization header cleared for HttpClient");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating authorization header: {ex.Message}");
            }
        }

        public async Task<ApiResponse> DeleteAsync(string url)
        {
            try
            {
                var response = await _httpClient.DeleteAsync(url);
                return await ProcessResponse(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ApiResponse<T>> GetAsync<T>(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                return await ProcessResponse<T>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<T>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest data)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, data, _jsonOptions);
                return await ProcessResponse<TResponse>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<TResponse>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ApiResponse<T>> PostAsync<T>(string url, object data)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, data, _jsonOptions);
                return await ProcessResponse<T>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<T>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ApiResponse> PostAsync(string url, object data)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, data, _jsonOptions);
                return await ProcessResponse(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ApiResponse<T>> PutAsync<T>(string url, object data)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync(url, data, _jsonOptions);
                return await ProcessResponse<T>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<T>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ApiResponse<TResponse>> PutAsync<TRequest, TResponse>(string url, TRequest data)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync(url, data, _jsonOptions);
                return await ProcessResponse<TResponse>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<TResponse>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ApiResponse> PutAsync(string url, object data)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync(url, data, _jsonOptions);
                return await ProcessResponse(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        private async Task<ApiResponse<T>> ProcessResponse<T>(HttpResponseMessage response)
        {
            var json = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"=== RAW JSON RESPONSE ===");
            System.Diagnostics.Debug.WriteLine(json);
            System.Diagnostics.Debug.WriteLine($"=== END RAW JSON ===");

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
                // Попробуем десериализовать в ApiResponse<T>
                var apiResponse = JsonSerializer.Deserialize<ApiResponse<T>>(json, _jsonOptions);
                if (apiResponse != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Deserialized as ApiResponse<T>, Data: {apiResponse.Data != null}");
                    return apiResponse;
                }

                // Если не получилось, попробуем десериализовать напрямую в T
                System.Diagnostics.Debug.WriteLine("Trying direct deserialization...");
                var directData = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                System.Diagnostics.Debug.WriteLine($"Direct deserialization result: {directData != null}");

                return new ApiResponse<T>
                {
                    Success = true,
                    Data = directData,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON Exception: {ex.Message}");
                return new ApiResponse<T>
                {
                    Success = false,
                    Error = $"Deserialization error: {ex.Message}",
                    Details = json,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        private async Task<ApiResponse> ProcessResponse(HttpResponseMessage response)
        {
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new ApiResponse
                {
                    Success = false,
                    Error = $"HTTP {response.StatusCode}",
                    Details = json,
                    Timestamp = DateTime.UtcNow
                };
            }

            try
            {
                var apiResponse = JsonSerializer.Deserialize<ApiResponse>(json, _jsonOptions);
                return apiResponse ?? new ApiResponse { Success = true, Timestamp = DateTime.UtcNow };
            }
            catch (JsonException ex)
            {
                return new ApiResponse
                {
                    Success = false,
                    Error = $"Deserialization error: {ex.Message}",
                    Details = json,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<Stream?> GetStreamAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                System.Diagnostics.Debug.WriteLine($"Image GET {url} -> {(int)response.StatusCode} {response.ReasonPhrase}");

                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Response content: {content}");
                    return null;
                }

                await using var networkStream = await response.Content.ReadAsStreamAsync();
                var memoryStream = new MemoryStream();
                await networkStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Image download error: {ex}");
                return null;
            }
        }

        public async Task<ApiResponse<T>> UploadFileAsync<T>(string url, Stream fileStream, string fileName, string contentType)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                content.Add(streamContent, "file", fileName);

                var response = await _httpClient.PostAsync(url, content);
                return await ProcessResponse<T>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<T>
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        } 
    }
}