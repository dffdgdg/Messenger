using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.DTO.User;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class UserController(IUserService userService, ILogger<UserController> logger) : BaseController<UserController>(logger)
    {
        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetAllUsers()
            => await ExecuteAsync(async () => await userService.GetAllUsersAsync(), "Пользователи получены успешно");

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<UserDTO>>> GetUser(int id) => await ExecuteAsync(async () =>
        {
            var user = await userService.GetUserAsync(id);
            return user ?? throw new KeyNotFoundException($"Пользователь с ID {id} не найден");
        });

        [HttpGet("online")]
        public async Task<ActionResult<ApiResponse<OnlineUsersResponseDTO>>> GetOnlineUsers() => await ExecuteAsync(async () =>
        {
            var onlineIds = await userService.GetOnlineUserIdsAsync();
            return new OnlineUsersResponseDTO
            {
                OnlineUserIds = onlineIds,
                TotalOnline = onlineIds.Count
            };
        }, "Список онлайн пользователей получен");

        [HttpGet("{id}/status")]
        public async Task<ActionResult<ApiResponse<OnlineStatusDTO>>> GetUserOnlineStatus(int id)
            => await ExecuteAsync(async () => await userService.GetOnlineStatusAsync(id));

        [HttpPost("status/batch")]
        public async Task<ActionResult<ApiResponse<List<OnlineStatusDTO>>>> GetUsersOnlineStatus([FromBody] List<int> userIds) => await ExecuteAsync(async () =>
        {
            if (userIds == null || userIds.Count == 0) throw new ArgumentException("Список ID пользователей не может быть пустым");
            return await userService.GetOnlineStatusesAsync(userIds);
        });

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDTO userDto) => await ExecuteAsync(async () =>
        {
            ValidateModel();
            if (id != userDto.Id) throw new ArgumentException("Несоответствие ID");
            await userService.UpdateUserAsync(id, userDto);
        }, "Пользователь обновлён успешно");

        [HttpPost("{id}/avatar")]
        public async Task<ActionResult<ApiResponse<AvatarResponseDTO>>> UploadAvatar(int id, IFormFile file) => await ExecuteAsync(async () =>
        {
            if (file == null || file.Length == 0) throw new ArgumentException("Файл не предоставлен");
            var avatarUrl = await userService.UploadAvatarAsync(id, file, Request);
            return new AvatarResponseDTO { AvatarUrl = avatarUrl };
        }, "Аватар загружен успешно");

        [HttpPut("{id}/username")]
        public async Task<IActionResult> ChangeUsername(int id, [FromBody] ChangeUsernameDTO dto)=> await ExecuteAsync(async () =>
        {
            ValidateModel();
            await userService.ChangeUsernameAsync(id, dto);
        }, "Username успешно изменён");

        [HttpPut("{id}/password")]
        public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDTO dto) => await ExecuteAsync(async () =>
        {
            ValidateModel();
            await userService.ChangePasswordAsync(id, dto);
        }, "Пароль успешно изменён");
    }
}