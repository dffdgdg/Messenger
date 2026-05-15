using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

public sealed class AuthController(IAuthService auth, IOptions<JwtSettings> jwtSettings,
    ILogger<AuthController> logger) : BaseController<AuthController>(logger)
{
    private const string RefreshTokenCookieName = "refresh_token";

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request.Username, request.Password, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            SetRefreshTokenCookie(result.Value.RefreshToken);
            return Map(Result<AuthResponseDto>.Success(result.Value.Response));
        }

        return Map(result.As<AuthResponseDto>());
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];

        if (string.IsNullOrEmpty(refreshToken))
            return Map(Result<TokenResponseDto>.Unauthorized("Refresh token отсутствует"));

        var result = await auth.RefreshTokenAsync(request.AccessToken, refreshToken, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            SetRefreshTokenCookie(result.Value.RefreshToken);
            return Map(Result<TokenResponseDto>.Success(result.Value.Response));
        }

        return Map(result.As<TokenResponseDto>());
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        var result = await auth.RevokeRefreshTokenAsync(GetCurrentUserId(), ct);

        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            Path = "/api/auth"
        });

        return Map(result);
    }

    private void SetRefreshTokenCookie(string refreshToken)
        => Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(jwtSettings.Value.RefreshTokenLifetimeDays),
            Path = "/api/auth"
        });
}