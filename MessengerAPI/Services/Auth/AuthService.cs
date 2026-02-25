using MessengerAPI.Common;
using MessengerAPI.Configuration;
using MessengerAPI.Mapping;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerShared.Dto.Auth;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerAPI.Services.Auth;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default);
}

public class AuthService : BaseService<AuthService>, IAuthService
{
    private readonly ITokenService _tokenService;
    private readonly MessengerSettings _settings;
    private readonly string _dummyHash;

    public AuthService(MessengerDbContext context,ITokenService tokenService,
        IOptions<MessengerSettings> settings, ILogger<AuthService> logger) : base(context, logger)
    {
        _tokenService = tokenService;
        _settings = settings.Value;
        _dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password", _settings.BcryptWorkFactor);
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username))
                return Result<AuthResponseDto>.Failure("Имя пользователя обязательно");

            if (string.IsNullOrWhiteSpace(password))
                return Result<AuthResponseDto>.Failure("Пароль обязателен");

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);

            if (user is null)
            {
                BCrypt.Net.BCrypt.Verify(password, _dummyHash);
                return Result<AuthResponseDto>.Failure(
                    "Неверное имя пользователя или пароль");
            }

            if (!VerifyPassword(password, user.PasswordHash))
            {
                _logger.LogWarning("Неудачная попытка входа: {Username}", username);
                return Result<AuthResponseDto>.Failure(
                    "Неверное имя пользователя или пароль");
            }

            if (user.IsBanned)
            {
                _logger.LogWarning("Попытка входа заблокированного пользователя: {Username}", username);
                return Result<AuthResponseDto>.Failure("Учётная запись заблокирована");
            }

            var role = await DetermineUserRoleAsync(user, ct);
            var token = _tokenService.GenerateToken(user.Id, role);

            var response = new AuthResponseDto
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.FormatDisplayName(),
                Token = token,
                Role = role
            };

            _logger.LogInformation("Успешный вход: {Username} (роль: {Role})",username, role);

            return Result<AuthResponseDto>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка авторизации: {Username}", username);
            return Result<AuthResponseDto>.Failure("Произошла ошибка при входе в систему");
        }
    }

    private async Task<UserRole> DetermineUserRoleAsync(Model.User user, CancellationToken ct)
    {
        if (user.DepartmentId == _settings.AdminDepartmentId)
            return UserRole.Admin;

        var isHead = await _context.Departments.AnyAsync(d => d.HeadId == user.Id, ct);

        return isHead ? UserRole.Head : UserRole.User;
    }

    private static bool VerifyPassword(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }
}