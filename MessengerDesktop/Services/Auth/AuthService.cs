using MessengerShared.Dto.Auth;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Auth;

public interface IAuthService
{
    Task<ApiResponse<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<ApiResponse<object>> ValidateTokenAsync(string token, CancellationToken ct = default);
    Task<ApiResponse<object>> LogoutAsync(string token, CancellationToken ct = default);
}

public class AuthService(HttpClient httpClient) : IAuthService
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<ApiResponse<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return ApiResponseHelper.Error<AuthResponseDto>("Имя пользователя и пароль обязательны");

            var loginDto = new LoginRequest(username, password.Trim());

            var response = await _httpClient.PostAsJsonAsync(ApiEndpoints.Auth.Login, loginDto, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                return ApiResponseHelper.Error<AuthResponseDto>($"Ошибка авторизации: {response.StatusCode}", errorContent);
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(ct);

            if (apiResponse != null)
            {
                return apiResponse;
            }

            try
            {
                var directData = await response.Content.ReadFromJsonAsync<AuthResponseDto>(ct);
                if (directData != null)
                {
                    return ApiResponseHelper.Success(directData, "Авторизация успешна");
                }
            }
            catch
            {
                // Ignore
            }

            return ApiResponseHelper.Error<AuthResponseDto>("Не удалось прочитать ответ сервера");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApiResponseHelper.Error<AuthResponseDto>($"Ошибка авторизации: {ex.Message}");
        }
    }

    public async Task<ApiResponse<object>> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(token)) return ApiResponseHelper.Error("Токен не предоставлен");

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.Auth.Validate);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, ct);

            return response.IsSuccessStatusCode
                ? ApiResponse<object>.Ok(null, "Токен действителен") : ApiResponseHelper.Error($"Токен недействителен: {response.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApiResponseHelper.Error($"Ошибка валидации: {ex.Message}");
        }
    }

    public async Task<ApiResponse<object>> LogoutAsync(string token, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.Auth.Logout);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, ct);

            return response.IsSuccessStatusCode ? ApiResponse<object>.Ok(null, "Выход выполнен успешно")
                : ApiResponseHelper.Error($"Ошибка выхода: {response.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApiResponseHelper.Error($"Ошибка выхода: {ex.Message}");
        }
    }
}