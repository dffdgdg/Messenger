using Core.Infrastructure.Helpers;
using Shared.Dto.Auth;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Services.Core.Auth;

public class AuthService(HttpClient httpClient) : IAuthService
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task PingAsync() => await _httpClient.GetAsync("/");

    public bool IsAccessTokenValid(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        try
        {
            if (!TokenHandler.CanReadToken(token))
                return false;

            var jwt = TokenHandler.ReadJwtToken(token);

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

            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = HttpResponseHelper.TryExtractErrorMessage(json, response.StatusCode);
                return ApiResponseHelper.Error<AuthResponseDto>(errorMessage, json);
            }

            try
            {
                var apiResponse = JsonSerializer.Deserialize<ApiResponse<AuthResponseDto>>(json, JsonOptions);
                if (apiResponse != null)
                    return apiResponse;
            }
            catch (JsonException) { /* Ожидаемо */ }

            return ApiResponseHelper.Error<AuthResponseDto>("Не удалось прочитать ответ сервера");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException)
        {
            return ApiResponseHelper.Error<AuthResponseDto>("Не удалось подключиться к серверу");
        }
        catch (Exception ex)
        {
            return ApiResponseHelper.Error<AuthResponseDto>($"Ошибка входа: {ex.Message}");
        }
    }

    public async Task<ApiResponse<TokenResponseDto>> RefreshTokenAsync(string accessToken, string? refreshToken = null, CancellationToken ct = default)
    {
        try
        {
            var request = new RefreshTokenRequest(accessToken);
            var response = await _httpClient.PostAsJsonAsync(ApiEndpoints.Auth.Refresh, request, ct);

            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = HttpResponseHelper.TryExtractErrorMessage(json, response.StatusCode);
                return ApiResponseHelper.Error<TokenResponseDto>(errorMessage, json);
            }

            try
            {
                var apiResponse = JsonSerializer.Deserialize<ApiResponse<TokenResponseDto>>(json, JsonOptions);
                if (apiResponse != null)
                    return apiResponse;
            }
            catch (JsonException) { /* Ожидаемо */ }

            return ApiResponseHelper.Error<TokenResponseDto>("Не удалось прочитать ответ сервера");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException)
        {
            return ApiResponseHelper.Error<TokenResponseDto>("Не удалось подключиться к серверу");
        }
        catch (Exception ex)
        {
            return ApiResponseHelper.Error<TokenResponseDto>($"Ошибка обновления токена: {ex.Message}");
        }
    }

    public async Task<ApiResponse<object>> RevokeAsync(string token, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, ApiEndpoints.Auth.Revoke);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, ct);

            return response.IsSuccessStatusCode
                ? ApiResponse<object>.Ok(null, "Выход выполнен успешно")
                : ApiResponseHelper.Error($"Ошибка выхода: {response.StatusCode}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ApiResponseHelper.Error($"Ошибка выхода: {ex.Message}");
        }
    }
}