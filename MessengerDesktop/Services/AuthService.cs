using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MessengerDesktop.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;
    private const string TokenKey = "auth_token";
    private const string UserIdKey = "user_id";

    public string? Token { get; private set; }
    public int? UserId { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    public AuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        LoadStoredAuth();
    }

    private void LoadStoredAuth()
    {
        try
        {
            Token = App.Current?.Storage.Get(TokenKey)?.ToString();
            var userIdStr = App.Current?.Storage.Get(UserIdKey)?.ToString();
            if (int.TryParse(userIdStr, out var userId))
            {
                UserId = userId;
            }
            if (!string.IsNullOrEmpty(Token))
            {
                App.UpdateHttpClientToken(Token);
            }
        }
        catch (Exception)
        {
            Token = null;
            UserId = null;
        }
    }

    private void SaveAuth(string token, int userId)
    {
        Token = token;
        UserId = userId;
        App.Current?.Storage.Set(TokenKey, token);
        App.Current?.Storage.Set(UserIdKey, userId.ToString());

        App.UpdateHttpClientToken(token);
        NotificationService.SetAuthToken(token);

        
    }

    public void ClearAuth()
    {
        Token = null;
        UserId = null;
        App.Current?.Storage.Remove(TokenKey);
        App.Current?.Storage.Remove(UserIdKey);

        App.UpdateHttpClientToken(null);
        NotificationService.SetAuthToken(null);
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"LoginAsync: username={username}");

            var response = await _httpClient.PostAsJsonAsync("api/auth/login", new { username, password });
            System.Diagnostics.Debug.WriteLine($"Login response status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var rawJson = await response.Content.ReadAsStringAsync();

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true 
                    };

                    var result = await response.Content.ReadFromJsonAsync<AuthResponse>(options);

                    if (result != null && result.Id > 0)
                    {
                        SaveAuth(result.Token, result.Id);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception jsonEx)
                {
                    return false;
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoginAsync exception: {ex.Message}");
            NotificationService.ShowError($"Ошибка входа: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string username, string password, string? displayName)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/auth/register",
                        new { username, password, displayName });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
                if (result != null)
                {
                    SaveAuth(result.Token, result.Id);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            NotificationService.ShowError($"Ошибка регистрации: {ex.Message}");
            return false;
        }
    }

    private class AuthResponse
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("DisplayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("Token")]
        public string Token { get; set; } = "";
    }
}