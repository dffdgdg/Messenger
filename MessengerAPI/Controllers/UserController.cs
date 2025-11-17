using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class UserController(IUserService userService, ILogger<UserController> logger) : BaseController<UserController>(logger)
    {
        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetAllUsers()
        {
            return await ExecuteAsync(async () =>
            {
                var users = await userService.GetAllUsersAsync();
                return users;
            }, "Пользователи получены успешно");
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<UserDTO>>> GetUser(int id)
        {
            return await ExecuteAsync(async () =>
            {
                var user = await userService.GetUserAsync(id);
                return user ?? throw new KeyNotFoundException($"Пользователь с ID {id} не найден");
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDTO userDto)
        {
            return await ExecuteAsync(async () =>
            {
                ValidateModel();

                if (id != userDto.Id)
                    throw new ArgumentException("ID mismatch");

                await userService.UpdateUserAsync(id, userDto);
            }, "Пользователь обновлен успешно");
        }

        [HttpPost("{id}/avatar")]
        public async Task<ActionResult> UploadAvatar(int id, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("Нет файла");

                var avatarUrl = await userService.UploadAvatarAsync(id, file, Request);

                var response = new { AvatarUrl = avatarUrl };
                return SuccessWithData(response, "Аватар загружен успешно");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Неверный файл или пользователь не найден для загрузки аватара {UserId}", id);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return InternalError(ex, "ОШИБКА загрузки аватара");
            }
        }
    }
}