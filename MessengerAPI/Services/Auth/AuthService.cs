using MessengerAPI.Services.Base;
using MessengerShared.Dto.Auth;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace MessengerAPI.Services.Auth;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<Result<TokenResponseDto>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default);
    Task<Result> RevokeRefreshTokenAsync(int userId, CancellationToken ct = default);
}

public sealed class AuthService : BaseService<AuthService>, IAuthService
{
    private readonly ITokenService _tokenService;
    private readonly MessengerSettings _settings;
    private readonly AppDateTime _appDateTime;
    private readonly IOptions<JwtSettings> _jwtSettings;
    private readonly string _dummyHash;

    public AuthService(
        MessengerDbContext context,
        ITokenService tokenService,
        IOptions<MessengerSettings> settings,
        IOptions<JwtSettings> jwtSettings,
        AppDateTime appDateTime,
        ILogger<AuthService> logger) : base(context, logger)
    {
        _tokenService = tokenService;
        _settings = settings.Value;
        _jwtSettings = jwtSettings;
        _appDateTime = appDateTime;
        _dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password", _settings.BcryptWorkFactor);
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(
        string username, string password, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username))
                return Result<AuthResponseDto>.Failure("Имя пользователя обязательно");

            if (string.IsNullOrWhiteSpace(password))
                return Result<AuthResponseDto>.Failure("Пароль обязателен");

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);

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
            var tokenPair = _tokenService.GenerateTokenPair(user.Id, role);

            // Сохраняем refresh token в БД
            await SaveRefreshTokenAsync(user.Id, tokenPair, ct);

            var response = new AuthResponseDto
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.FormatDisplayName(),
                Token = tokenPair.AccessToken,
                RefreshToken = tokenPair.RefreshToken,
                Role = role
            };

            _logger.LogInformation("Успешный вход: {Username} (роль: {Role})",
                username, role);

            return Result<AuthResponseDto>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка авторизации: {Username}", username);
            return Result<AuthResponseDto>.Failure("Произошла ошибка при входе в систему");
        }
    }

    public async Task<Result<TokenResponseDto>> RefreshTokenAsync(
        string accessToken, string refreshToken, CancellationToken ct = default)
    {
        try
        {
            // 1. Извлекаем claims из истёкшего access token
            var principal = _tokenService.GetPrincipalFromExpiredToken(accessToken);
            if (principal is null)
                return Result<TokenResponseDto>.Failure("Недействительный access token");

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

            if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrEmpty(jtiClaim))
                return Result<TokenResponseDto>.Failure("Недействительный access token");

            // 2. Находим refresh token в БД по хешу
            var refreshTokenHash = ITokenService.HashToken(refreshToken);

            var storedToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt =>
                    rt.TokenHash == refreshTokenHash &&
                    rt.UserId == userId, ct);

            if (storedToken is null)
            {
                _logger.LogWarning(
                    "Refresh token не найден для пользователя {UserId}", userId);
                return Result<TokenResponseDto>.Failure("Недействительный refresh token");
            }

            // 3. Проверяем: если токен уже использован — это replay attack
            if (storedToken.UsedAt != null || storedToken.RevokedAt != null)
            {
                _logger.LogWarning(
                    "Обнаружено повторное использование refresh token! " +
                    "UserId={UserId}, FamilyId={FamilyId}. Отзываем всю семью.",
                    userId, storedToken.FamilyId);

                // Отзываем ВСЮ семью ротации — потенциальная компрометация
                await RevokeTokenFamilyAsync(storedToken.FamilyId, ct);

                return Result<TokenResponseDto>.Failure("Refresh token уже использован. Авторизуйтесь заново.");
            }

            // 4. Проверяем срок действия
            if (storedToken.ExpiresAt <= _appDateTime.UtcNow)
            {
                _logger.LogWarning(
                    "Истёкший refresh token для пользователя {UserId}", userId);
                return Result<TokenResponseDto>.Failure("Refresh token истёк");
            }

            // 5. Проверяем, что пользователь не забанен
            var user = storedToken.User;
            if (user.IsBanned)
            {
                await RevokeTokenFamilyAsync(storedToken.FamilyId, ct);
                return Result<TokenResponseDto>.Failure("Учётная запись заблокирована");
            }

            // 6. Ротация: помечаем старый как использованный
            var role = await DetermineUserRoleAsync(user, ct);
            var newTokenPair = _tokenService.GenerateTokenPair(userId, role);

            var newRefreshToken = new Model.RefreshToken
            {
                UserId = userId,
                TokenHash = ITokenService.HashToken(newTokenPair.RefreshToken),
                JwtId = newTokenPair.JwtId,
                CreatedAt = _appDateTime.UtcNow,
                ExpiresAt = _appDateTime.UtcNow.AddDays(_jwtSettings.Value.RefreshTokenLifetimeDays),
                FamilyId = storedToken.FamilyId // Та же семья
            };

            _context.RefreshTokens.Add(newRefreshToken);

            storedToken.UsedAt = _appDateTime.UtcNow;
            // ReplacedByTokenId установим после SaveChanges, когда у newRefreshToken будет Id

            await _context.SaveChangesAsync(ct);

            // Обновляем ссылку
            storedToken.ReplacedByTokenId = newRefreshToken.Id;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Refresh token ротирован для пользователя {UserId}", userId);

            return Result<TokenResponseDto>.Success(new TokenResponseDto
            {
                Token = newTokenPair.AccessToken,
                RefreshToken = newTokenPair.RefreshToken,
                UserId = userId,
                Role = role
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обновления токена");
            return Result<TokenResponseDto>.Failure("Произошла ошибка при обновлении токена");
        }
    }

    public async Task<Result> RevokeRefreshTokenAsync(int userId, CancellationToken ct = default)
    {
        try
        {
            var now = _appDateTime.UtcNow;

            var activeTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.UsedAt == null)
                .ToListAsync(ct);

            foreach (var token in activeTokens)
            {
                token.RevokedAt = now;
            }

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Отозвано {Count} refresh tokens для пользователя {UserId}",
                activeTokens.Count, userId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка отзыва токенов для пользователя {UserId}", userId);
            return Result.Failure("Ошибка при отзыве токенов");
        }
    }

    #region Private Methods

    private async Task SaveRefreshTokenAsync(
        int userId, TokenPair tokenPair, CancellationToken ct)
    {
        var refreshTokenEntity = new Model.RefreshToken
        {
            UserId = userId,
            TokenHash = ITokenService.HashToken(tokenPair.RefreshToken),
            JwtId = tokenPair.JwtId,
            CreatedAt = _appDateTime.UtcNow,
            ExpiresAt = _appDateTime.UtcNow.AddDays(_jwtSettings.Value.RefreshTokenLifetimeDays),
            FamilyId = Guid.NewGuid().ToString() // Новая семья при логине
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync(ct);

        // Очистка старых токенов (опционально, чтобы таблица не разрасталась)
        await CleanupExpiredTokensAsync(userId, ct);
    }

    private async Task RevokeTokenFamilyAsync(string familyId, CancellationToken ct)
    {
        var now = _appDateTime.UtcNow;

        var familyTokens = await _context.RefreshTokens
            .Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in familyTokens)
        {
            token.RevokedAt = now;
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Отозвано {Count} токенов семьи {FamilyId}",
            familyTokens.Count, familyId);
    }

    private async Task CleanupExpiredTokensAsync(int userId, CancellationToken ct)
    {
        var cutoff = _appDateTime.UtcNow.AddDays(-60); // Удаляем старше 60 дней

        var expiredCount = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (expiredCount > 0)
        {
            _logger.LogDebug(
                "Очищено {Count} истёкших refresh tokens для пользователя {UserId}",
                expiredCount, userId);
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

    #endregion
}