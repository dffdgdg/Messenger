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

                Console.WriteLine($"TOKEN WITH BEARER: Bearer {data.Token}");
                return data;
            }, "Авторизация прошла успешно");
        }
    }
}