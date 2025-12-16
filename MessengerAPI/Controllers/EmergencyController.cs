using MessengerAPI.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class EmergencyController(MessengerDbContext context, ILogger<EmergencyController> logger) : ControllerBase
    {
        [HttpPost("reset-all-passwords")]
        [AllowAnonymous]
        public async Task<ActionResult> ResetAllPasswordsToDefault()
        {
            try
            {
                

                var users = await context.Users.ToListAsync();
                var resetCount = 0;

                foreach (var user in users)
                {
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("123", 12);
                    resetCount++;
                }

                await context.SaveChangesAsync();

                logger.LogCritical($"АВАРИЙНЫЙ СБРОС: Сброшены пароли для {resetCount} пользователей на '123'");

                return Ok(new
                {
                    message = $"Пароли сброшены для {resetCount} пользователей",
                    defaultPassword = "123",
                    warning = "НЕМЕДЛЕННО УДАЛИТЕ ЭТОТ ЭНДПОИНТ ПОСЛЕ ИСПОЛЬЗОВАНИЯ!"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при аварийном сбросе паролей");
                return StatusCode(500, new { error = "Ошибка сервера" });
            }
        }

        /// <summary>
        /// Сброс пароля только администраторов
        /// </summary>
        [HttpPost("reset-admin-passwords")]
        [AllowAnonymous]
        public async Task<ActionResult> ResetAdminPasswords()
        {
            try
            {
                var secretKey = Request.Headers["X-Emergency-Key"].FirstOrDefault();
                if (secretKey != "vRQHb2XkyCqD7hZP9xjMwN5tF3gAS4Ue") // Измените этот ключ
                {
                    return Unauthorized();
                }

                var admins = await context.Users
                    .Where(u => u.Department == 1) // Департамент 1 = админы
                    .ToListAsync();

                var resetCount = 0;
                foreach (var admin in admins)
                {
                    admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword("123", 12);
                    resetCount++;
                }

                await context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Сброшены пароли {resetCount} администраторов",
                    usernames = admins.Select(a => a.Username).ToList()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка сброса паролей администраторов");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Сброс пароля конкретного пользователя по имени
        /// </summary>
        [HttpPost("reset-user-password")]
        [AllowAnonymous]
        public async Task<ActionResult> ResetUserPassword([FromQuery] string username)
        {
            try
            {
                var secretKey = Request.Headers["X-Emergency-Key"].FirstOrDefault();
                if (secretKey != "vRQHb2XkyCqD7hZP9xjMwN5tF3gAS4Ue") // Измените этот ключ
                {
                    return Unauthorized();
                }

                var user = await context.Users
                    .FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                {
                    return NotFound(new { error = $"Пользователь '{username}' не найден" });
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("123", 12);
                await context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Пароль для '{username}' сброшен на '123'",
                    userId = user.Id,
                    displayName = user.DisplayName
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка сброса пароля пользователя");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}