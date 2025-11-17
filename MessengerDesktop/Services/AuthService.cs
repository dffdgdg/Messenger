using CommunityToolkit.Mvvm.ComponentModel;
using MessengerShared.DTO;
using MessengerShared.Response;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    public partial class AuthService : ObservableObject
    {
        private readonly HttpClient _httpClient;
        private readonly ISecureStorageService _secureStorage;

        private const string TokenKey = "auth_token";
        private const string UserIdKey = "user_id";

        [ObservableProperty]
        private int? userId;

        [ObservableProperty]
        private string? token;

        [ObservableProperty]
        private bool isAuthenticated;

        [ObservableProperty]
        private bool isInitialized = false;

        public AuthService(HttpClient httpClient, ISecureStorageService secureStorage)
        {
            _httpClient = httpClient;
            _secureStorage = secureStorage;
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var storedToken = await _secureStorage.GetAsync<string>(TokenKey);
                UserId = await _secureStorage.GetAsync<int?>(UserIdKey);

                if (!string.IsNullOrEmpty(storedToken) && UserId.HasValue)
                {
                    Token = storedToken;
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", storedToken);
                    IsAuthenticated = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadStoredAuth error: {ex.Message}");
                await ClearAuthAsync();
            }
            finally
            {
                IsInitialized = true; 
            }
        }

        public async Task WaitForInitializationAsync()
        {
            while (!IsInitialized)
            {
                await Task.Delay(10);
            }
        }

        private async Task SaveAuthAsync(string newToken, int newUserId)
        {
            await _secureStorage.SaveAsync(TokenKey, newToken);
            await _secureStorage.SaveAsync(UserIdKey, newUserId);

            Token = newToken;
            UserId = newUserId;
            IsAuthenticated = true;

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newToken);
        }

        public async Task ClearAuthAsync()
        {
            await _secureStorage.RemoveAsync(TokenKey);
            await _secureStorage.RemoveAsync(UserIdKey);

            Token = null;
            UserId = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
            IsAuthenticated = false;
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    return false;

                var loginDto = new LoginDTO { Username = username.Trim(), Password = password };
                var response = await _httpClient.PostAsJsonAsync("api/auth/login", loginDto);

                if (!response.IsSuccessStatusCode)
                    return false;

                var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDTO>>();
                if (apiResponse?.Success == true && apiResponse.Data?.Id > 0)
                {
                    await SaveAuthAsync(apiResponse.Data.Token, apiResponse.Data.Id);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}