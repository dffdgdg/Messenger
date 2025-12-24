using MessengerAPI.Services;
using MessengerShared.DTO.Auth;
using MessengerShared.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class AuthController(IAuthService authService, ILogger<AuthController> logger) : BaseController<AuthController>(logger)
    {
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<AuthResponseDTO>>> Login([FromBody] LoginRequest request) => 
            await ExecuteAsync(async () =>
            {
                ValidateModel();
                (bool success, AuthResponseDTO? data, string? error) = await authService.LoginAsync(request.Username, request.Password);
                if (!success) throw new ArgumentException(error);
                return data!;
            }, "Авторизация прошла успешно");
    }
}