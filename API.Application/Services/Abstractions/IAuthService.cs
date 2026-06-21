using API.Application.Services.Core.Auth;
using API.Domain.Common;

namespace API.Application.Services.Abstractions;

public interface IAuthService
{
    Task<Result<AuthLoginResult>> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<Result<AuthRefreshResult>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default);
    Task<Result> RevokeRefreshTokenAsync(int userId, CancellationToken ct = default);
}