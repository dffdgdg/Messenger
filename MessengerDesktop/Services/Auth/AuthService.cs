using MessengerShared.Dto.Auth;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Auth;

public interface IAuthService
{
    Task<ApiResponse<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<ApiResponse<TokenResponseDto>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default);
    Task<ApiResponse<object>> RevokeAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Локальная проверка: не истёк ли access token.
    /// Не делает сетевой запрос — читает exp claim из JWT.
    /// </summary>
    bool IsAccessTokenValid(string token);
}

public class AuthService(HttpClient httpClient) : IAuthService
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public bool IsAccessTokenValid(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        try
        {
            // Читаем JWT без валидации подписи — нам нужен только exp
            if (!TokenHandler.CanReadToken(token))
                return false;

            var jwt = TokenHandler.ReadJwtToken(token);

            // Проверяем срок действия с небольшим запасом (30 секунд)
            // Если до истечения осталось меньше 30 секунд — считаем невалидным,
            // чтобы refresh произошёл заранее
            const int bufferSeconds = 30;
            return jwt.ValidTo > DateTime.UtcNow.AddSeconds(bufferSeconds);
        }
        catch
        {
            return false;
        }
    }

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
                return apiResponse;

            try
            {
                var directData = await response.Content.ReadFromJsonAsync<AuthResponseDto>(ct);
                if (directData != null)
                    return ApiResponseHelper.Success(directData, "Авторизация успешна");
            }
            catch { /* Ignore */ }

            return ApiResponseHelper.Error<AuthResponseDto>("Не удалось прочитать ответ сервера");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ApiResponseHelper.Error<AuthResponseDto>($"Ошибка авторизации: {ex.Message}");
        }
    }

    public async Task<ApiResponse<TokenResponseDto>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default)
    {
        try
        {
            var request = new RefreshTokenRequest(accessToken, refreshToken);
            var response = await _httpClient.PostAsJsonAsync(ApiEndpoints.Auth.Refresh, request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                return ApiResponseHelper.Error<TokenResponseDto>($"Ошибка обновления токена: {response.StatusCode}", errorContent);
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<TokenResponseDto>>(ct);

            return apiResponse ?? ApiResponseHelper.Error<TokenResponseDto>("Не удалось прочитать ответ сервера");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ApiResponseHelper.Error<TokenResponseDto>($"Ошибка обновления токена: {ex.Message}"); }
    }

    public async Task<ApiResponse<object>> RevokeAsync(string token, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.Auth.Revoke);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, ct);

            return response.IsSuccessStatusCode ? ApiResponse<object>.Ok(null, "Выход выполнен успешно")
                : ApiResponseHelper.Error($"Ошибка выхода: {response.StatusCode}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ApiResponseHelper.Error($"Ошибка выхода: {ex.Message}"); }
    }
}