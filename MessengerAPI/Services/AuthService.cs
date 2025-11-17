using MessengerAPI.Helpers;
using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace MessengerAPI.Services
{
    public interface IAuthService
    {
        Task<(bool Success, AuthResponseDTO? Data, string? Error)> LoginAsync(string username, string password);
        Task<(bool Success, AuthResponseDTO? Data, string? Error)> RegisterAsync(string username, string password, string? displayName);
    }

    public class AuthService(MessengerDbContext context,ITokenService tokenService,ILogger<AuthService> logger) 
        : BaseService<AuthService>(context, logger), IAuthService
    {
        public async Task<(bool Success, AuthResponseDTO? Data, string? Error)> LoginAsync(string username, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return (false, null, "Username is required");

                if (string.IsNullOrWhiteSpace(password))
                    return (false, null, "Password is required");

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username.Trim());

                if (user == null)
                    return (false, null, "Пользователь не найден");

                using var hmac = new HMACSHA256(Convert.FromBase64String(user.PasswordSalt));
                var hash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(password)));

                if (hash != user.PasswordHash)
                    return (false, null, "Неверный пароль");

                var token = tokenService.GenerateToken(user.Id);

                var dto = new AuthResponseDTO
                {
                    Id = user.Id,
                    Username = user.Username,
                    DisplayName = user.DisplayName,
                    Token = token
                };

                _logger.LogInformation("User {Username} successfully logged in", username);
                return (true, dto, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user {Username}", username);
                return (false, null, "Произошла ошибка при входе в систему");
            }
        }

        public async Task<(bool Success, AuthResponseDTO? Data, string? Error)> RegisterAsync(string username, string password, string? displayName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return (false, null, "Username is required");

                if (string.IsNullOrWhiteSpace(password))
                    return (false, null, "Password is required");

                if (password.Length < 6)
                    return (false, null, "Password must be at least 6 characters long");

                username = username.Trim();
                displayName = displayName?.Trim();

                if (await _context.Users.AnyAsync(u => u.Username == username))
                    return (false, null, "Пользователь с таким именем уже существует");

                using var hmac = new HMACSHA256();
                var salt = Convert.ToBase64String(hmac.Key);
                var hash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(password)));

                var user = new User
                {
                    Username = username,
                    DisplayName = displayName ?? username,
                    PasswordSalt = salt,
                    PasswordHash = hash,
                    CreatedAt = DateTime.UtcNow
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

                var token = tokenService.GenerateToken(user.Id);

                var dto = new AuthResponseDTO
                {
                    Id = user.Id,
                    Username = user.Username,
                    DisplayName = user.DisplayName,
                    Token = token
                };

                _logger.LogInformation("User {Username} successfully registered with ID {UserId}", username, user.Id);
                return (true, dto, null);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error during registration for user {Username}", username);
                return (false, null, "Произошла ошибка при регистрации пользователя");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for user {Username}", username);
                return (false, null, "Произошла ошибка при регистрации");
            }
        }
    }
}