using MessengerAPI.Common;
using MessengerAPI.Configuration;
using MessengerAPI.Helpers;
using MessengerAPI.Model;
using MessengerShared.DTO.Auth;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerAPI.Services
{
    public interface IAuthService
    {
        Task<Result<AuthResponseDTO>> LoginAsync(string username, string password);
    }

    public class AuthService : BaseService<AuthService>, IAuthService
    {
        private readonly ITokenService _tokenService;
        private readonly MessengerSettings _settings;
        private readonly string _dummyHash;

        public AuthService(MessengerDbContext context,ITokenService tokenService,IOptions<MessengerSettings> settings,ILogger<AuthService> logger) : base(context, logger)
        {
            _tokenService = tokenService;
            _settings = settings.Value;
            _dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password", _settings.BcryptWorkFactor);
        }

        public async Task<Result<AuthResponseDTO>> LoginAsync(string username, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return Result<AuthResponseDTO>.Failure("Имя пользователя обязательно");

                if (string.IsNullOrWhiteSpace(password))
                    return Result<AuthResponseDTO>.Failure("Пароль обязателен");

                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username.Trim());

                if (user is null)
                {
                    BCrypt.Net.BCrypt.Verify(password, _dummyHash);
                    return Result<AuthResponseDTO>.Failure("Неверное имя пользователя или пароль");
                }

                if (!VerifyPassword(password, user.PasswordHash))
                {
                    _logger.LogWarning("Неудачная попытка входа: {Username}", username);
                    return Result<AuthResponseDTO>.Failure("Неверное имя пользователя или пароль");
                }

                var role = await DetermineUserRoleAsync(user);
                var token = _tokenService.GenerateToken(user.Id, role);

                var response = new AuthResponseDTO
                {
                    Id = user.Id,
                    Username = user.Username,
                    DisplayName = FormatDisplayName(user),
                    Token = token,
                    Role = role
                };

                _logger.LogInformation("Успешный вход: {Username} (роль: {Role})", username, role);

                return Result<AuthResponseDTO>.Success(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка авторизации: {Username}", username);
                return Result<AuthResponseDTO>.Failure("Произошла ошибка при входе в систему");
            }
        }

        private async Task<UserRole> DetermineUserRoleAsync(User user)
        {
            if (user.Department == _settings.AdminDepartmentId) 
                return UserRole.Admin;

            var isHead = await _context.Departments.AnyAsync(d => d.Head == user.Id);

            return isHead ? UserRole.Head : UserRole.User;
        }

        private static bool VerifyPassword(string password, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch
            {
                return false;
            }
        }

        private static string FormatDisplayName(User user)
        {
            var parts = new[] { user.Surname, user.Name, user.Midname }.Where(p => !string.IsNullOrWhiteSpace(p));

            return string.Join(" ", parts);
        }
    }
}