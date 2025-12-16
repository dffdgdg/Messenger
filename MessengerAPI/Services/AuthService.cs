using MessengerAPI.Helpers;
using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IAuthService
    {
        Task<(bool Success, AuthResponseDTO? Data, string? Error)> LoginAsync(string username, string password);
        Task<(bool Success, AuthResponseDTO? Data, string? Error)> RegisterAsync(string username, string password, string? displayName);
        Task ResetPasswordAsync(string username, string newPassword);
    }

    public class AuthService(MessengerDbContext context, ITokenService tokenService, ILogger<AuthService> logger) : BaseService<AuthService>(context, logger), IAuthService
    {
        private const int BcryptWorkFactor = 12;
        private const int AdminDepartmentId = 1;

        private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password_for_timing", BcryptWorkFactor);

        public async Task<(bool Success, AuthResponseDTO? Data, string? Error)> LoginAsync(string username, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return (false, null, "Имя пользователя обязательно");

                if (string.IsNullOrWhiteSpace(password))
                    return (false, null, "Пароль обязателен");

                var user = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Username == username.Trim());

                if (user == null)
                {
                    BCrypt.Net.BCrypt.Verify(password, DummyHash);
                    return (false, null, "Неверное имя пользователя или пароль");
                }

                if (!VerifyPassword(password, user.PasswordHash))
                {
                    _logger.LogWarning("Неудачная попытка входа для пользователя {Username}", username);
                    return (false, null, "Неверное имя пользователя или пароль");
                }

                if (NeedsRehash(user.PasswordHash))
                {
                    await UpdatePasswordHashAsync(user.Id, password);
                }

                var role = GetUserRole(user.Department);
                var token = tokenService.GenerateToken(user.Id, role);

                var dto = new AuthResponseDTO
                {
                    Id = user.Id,
                    Username = user.Username,
                    DisplayName = user.DisplayName,
                    Token = token
                };

                _logger.LogInformation("Пользователь {Username} успешно авторизован (роль: {Role})",
                    username, role ?? "User");
                return (true, dto, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка авторизации пользователя {Username}", username);
                return (false, null, "Произошла ошибка при входе в систему");
            }
        }

        public async Task ResetPasswordAsync(string username, string newPassword)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username)
                ?? throw new KeyNotFoundException($"Пользователь '{username}' не найден");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
                throw new ArgumentException("Пароль должен содержать минимум 8 символов");

            user.PasswordHash = HashPassword(newPassword);

            await SaveChangesAsync();

            _logger.LogInformation("Пароль сброшен для пользователя {Username}", username);
        }

        public async Task<(bool Success, AuthResponseDTO? Data, string? Error)> RegisterAsync(string username, string password, string? displayName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return (false, null, "Имя пользователя обязательно");

                if (string.IsNullOrWhiteSpace(password))
                    return (false, null, "Пароль обязателен");

                if (password.Length < 8)
                    return (false, null, "Пароль должен содержать минимум 8 символов");

                if (!IsPasswordStrong(password))
                    return (false, null, "Пароль должен содержать буквы и цифры");

                username = username.Trim();
                displayName = displayName?.Trim();

                if (await _context.Users.AnyAsync(u => u.Username == username))
                    return (false, null, "Пользователь с таким именем уже существует");

                var user = new User
                {
                    Username = username,
                    DisplayName = displayName ?? username,
                    PasswordHash = HashPassword(password),
                    CreatedAt = DateTime.Now
                };

                _context.Users.Add(user);
                await SaveChangesAsync();

                var userSetting = new UserSetting
                {
                    UserId = user.Id,
                    NotificationsEnabled = true,
                    CanBeFoundInSearch = true
                };

                _context.UserSettings.Add(userSetting);
                await SaveChangesAsync();

                var role = GetUserRole(user.Department);
                var token = tokenService.GenerateToken(user.Id, role);

                var dto = new AuthResponseDTO
                {
                    Id = user.Id,
                    Username = user.Username,
                    DisplayName = user.DisplayName,
                    Token = token
                };

                _logger.LogInformation("Пользователь {Username} зарегистрирован с ID {UserId}", username, user.Id);
                return (true, dto, null);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Ошибка базы данных при регистрации пользователя {Username}", username);
                return (false, null, "Произошла ошибка при регистрации пользователя");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка регистрации пользователя {Username}", username);
                return (false, null, "Произошла ошибка при регистрации");
            }
        }

        #region Role Helper

        private static string? GetUserRole(int? departmentId) => departmentId == AdminDepartmentId ? "Admin" : null;

        #endregion

        #region Password Helpers

        private async Task UpdatePasswordHashAsync(int userId, string password)
        {
            try
            {
                User? user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    user.PasswordHash = HashPassword(password);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Хеш пароля обновлён для пользователя {UserId}", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось обновить хеш пароля для пользователя {UserId}", userId);
            }
        }

        private static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);

        private static bool VerifyPassword(string password, string hash)
        {
            try
            {
                return !IsLegacyHash(hash) && BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch
            {
                return false;
            }
        }

        private static bool NeedsRehash(string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.PasswordNeedsRehash(hash, BcryptWorkFactor);
            }
            catch
            {
                return true;
            }
        }

        private static bool IsLegacyHash(string hash) => !hash.StartsWith("$2");

        private static bool IsPasswordStrong(string password) => password.Any(char.IsLetter) && password.Any(char.IsDigit);

        #endregion
    }
}