using MessengerAPI.Services.Auth;
using MessengerShared.Dto.Auth;
using MessengerShared.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MessengerAPI.Controllers;

public class AuthController(IAuthService authService, ILogger<AuthController> logger)
    : BaseController<AuthController>(logger)
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginRequest request, CancellationToken ct)
        => await ExecuteAsync(() => authService.LoginAsync(request.Username, request.Password, ct), "Авторизация прошла успешно");
}