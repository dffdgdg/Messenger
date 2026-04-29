using MessengerShared.Dto.Auth;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Abstractions;

public interface IAuthService
{
    Task<ApiResponse<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<ApiResponse<TokenResponseDto>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default);
    Task<ApiResponse<object>> RevokeAsync(string token, CancellationToken ct = default);
    Task PingAsync();

    /// <summary>
    /// Локальная проверка: не истёк ли access token.
    /// Не делает сетевой запрос — читает exp claim из JWT.
    /// </summary>
    bool IsAccessTokenValid(string token);
}
