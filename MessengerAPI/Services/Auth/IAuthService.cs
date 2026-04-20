using MessengerShared.Dto.Auth;

namespace MessengerAPI.Services.Auth;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<Result<TokenResponseDto>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default);
    Task<Result> RevokeRefreshTokenAsync(int userId, CancellationToken ct = default);
}