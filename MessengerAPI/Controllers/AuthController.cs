using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class AuthController(IAuthService authService, ILogger<AuthController> logger) : BaseController<AuthController>(logger)
    {
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<AuthResponseDTO>>> Login([FromBody] LoginDTO request)
        {
            return await ExecuteAsync(async () =>
            {
                ValidateModel();
                var (success, data, error) = await authService.LoginAsync(request.Username, request.Password);

                if (!success)
                    throw new ArgumentException(error);

                return data!;
            }, "Авторизация прошла успешно");
        }

        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<ActionResult<ApiResponse<AuthResponseDTO>>> Register([FromBody] RegisterDTO request)
        {
            return await ExecuteAsync(async () =>
            {
                ValidateModel();
                var (success, data, error) = await authService.RegisterAsync(
                    request.Username,
                    request.Password,
                    request.DisplayName);

                if (!success)
                    throw new ArgumentException(error);

                return data!;
            }, "Регистрация прошла успешно");
        }
        
    }
}