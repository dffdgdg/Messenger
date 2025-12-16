using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.Services.Auth;
using MessengerShared.DTO;
using MessengerShared.Response;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.Services;

public partial class AuthService : ObservableObject, IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ISecureStorageService _secureStorage;
    private readonly TaskCompletionSource _initializationTcs = new();

    private const string TokenKey = "auth_token";
    private const string UserIdKey = "user_id";

    [ObservableProperty]
    private int? userId;

    [ObservableProperty]
    private string? token;

    [ObservableProperty]
    private bool isAuthenticated;

    [ObservableProperty]
    private bool isInitialized;

    public AuthService(HttpClient httpClient, ISecureStorageService secureStorage)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _ = InitializeInternalAsync();
    }

    private async Task InitializeInternalAsync()
    {
        try
        {
            await LoadStoredAuthAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AuthService initialization error: {ex.Message}");
            await ClearAuthAsync();
        }
        finally
        {
            IsInitialized = true;
            _initializationTcs.TrySetResult();
        }
    }

    private async Task LoadStoredAuthAsync()
    {
        var storedToken = await _secureStorage.GetAsync<string>(TokenKey);
        var storedUserId = await _secureStorage.GetAsync<int?>(UserIdKey);

        if (!string.IsNullOrEmpty(storedToken) && storedUserId.HasValue)
        {
            var isValid = await ValidateTokenAsync(storedToken);

            if (isValid)
            {
                Token = storedToken;
                UserId = storedUserId;
                UpdateHttpClientAuthorization(storedToken);
                IsAuthenticated = true;
            }
            else
            {
                await ClearAuthAsync();
            }
        }
    }
    private async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            // ¬ременно устанавливаем токен дл€ проверки
            var oldAuth = _httpClient.DefaultRequestHeaders.Authorization;
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // ƒелаем простой запрос дл€ проверки токена
            var response = await _httpClient.GetAsync("api/auth/validate");

            // ¬осстанавливаем оригинальный заголовок
            _httpClient.DefaultRequestHeaders.Authorization = oldAuth;

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    public Task WaitForInitializationAsync() => _initializationTcs.Task;

    public async Task<bool> WaitForInitializationAsync(TimeSpan timeout)
    {
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_initializationTcs.Task, timeoutTask);
        return completedTask == _initializationTcs.Task;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return false;

            var loginDto = new LoginDTO
            {
                Username = username.Trim(),
                Password = password
            };

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Login error: {ex.Message}");
            return false;
        }
    }

    private async Task SaveAuthAsync(string newToken, int newUserId)
    {
        await _secureStorage.SaveAsync(TokenKey, newToken);
        await _secureStorage.SaveAsync(UserIdKey, newUserId);

        Token = newToken;
        UserId = newUserId;
        IsAuthenticated = true;

        UpdateHttpClientAuthorization(newToken);
    }

    public async Task ClearAuthAsync()
    {
        await _secureStorage.RemoveAsync(TokenKey);
        await _secureStorage.RemoveAsync(UserIdKey);

        Token = null;
        UserId = null;
        IsAuthenticated = false;

        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private void UpdateHttpClientAuthorization(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
}