using MessengerAPI.Services.Auth;
using MessengerShared.Dto.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace MessengerAPI.Controllers;

public sealed class AuthController(IAuthService authService, ILogger<AuthController> logger) : BaseController<AuthController>(logger)
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginRequest request, CancellationToken ct)
        => await ExecuteAsync(() => authService.LoginAsync(request.Username, request.Password, ct), "Авторизация прошла успешно");

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<TokenResponseDto>>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => await ExecuteAsync(() => authService.RefreshTokenAsync(request.AccessToken, request.RefreshToken, ct), "Токен обновлён");

    /// <summary>
    /// Отзывает все refresh-токены текущего пользователя (logout).
    /// </summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
        => await ExecuteAsync(() => authService.RevokeRefreshTokenAsync(GetCurrentUserId(), ct), "Все токены отозваны");
}