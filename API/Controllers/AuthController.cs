using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

public sealed class AuthController(IAuthService auth, ILogger<AuthController> logger) : BaseController<AuthController>(logger)
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Map(await auth.LoginAsync(request.Username, request.Password, ct));

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => Map(await auth.RefreshTokenAsync(request.AccessToken, request.RefreshToken, ct));

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
        => Map(await auth.RevokeRefreshTokenAsync(GetCurrentUserId(), ct));
}