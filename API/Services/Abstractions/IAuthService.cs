using API.Services.Core.Auth;

namespace API.Services.Abstractions;

public interface IAuthService
{
    Task<Result<AuthLoginResult>> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<Result<AuthRefreshResult>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default);
    Task<Result> RevokeRefreshTokenAsync(int userId, CancellationToken ct = default);
}