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
    private const int MaxActiveSessions = 5;

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

    public async Task<Result<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return Result<AuthResponseDto>.Failure("Имя пользователя обязательно");

        if (string.IsNullOrWhiteSpace(password))
            return Result<AuthResponseDto>.Failure("Пароль обязателен");

        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);

        if (user is null)
        {
            BCrypt.Net.BCrypt.Verify(password, _dummyHash);
            return Result<AuthResponseDto>.Failure("Неверное имя пользователя или пароль");
        }

        if (!VerifyPassword(password, user.PasswordHash))
        {
            _logger.LogWarning("Неудачная попытка входа: {Username}", username);
            return Result<AuthResponseDto>.Failure("Неверное имя пользователя или пароль");
        }

        if (user.IsBanned)
        {
            _logger.LogWarning("Попытка входа заблокированного пользователя: {Username}", username);
            return Result<AuthResponseDto>.Forbidden("Учётная запись заблокирована");
        }

        var role = await DetermineUserRoleAsync(user, ct);
        var tokenPair = _tokenService.GenerateTokenPair(user.Id, role);

        var saveResult = await SaveRefreshTokenAsync(user.Id, tokenPair, ct);
        if (saveResult.IsFailure)
            return Result<AuthResponseDto>.Internal("Ошибка при сохранении сессии");

        var response = new AuthResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.FormatDisplayName(),
            Token = tokenPair.AccessToken,
            RefreshToken = tokenPair.RefreshToken,
            Role = role
        };

        _logger.LogInformation("Успешный вход: {Username} (роль: {Role})", username, role);

        return Result<AuthResponseDto>.Success(response);
    }

    public async Task<Result<TokenResponseDto>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default)
    {
        var principalResult = _tokenService.GetPrincipalFromExpiredToken(accessToken);
        if (principalResult.IsFailure)
            return Result<TokenResponseDto>.FromFailure(principalResult);

        var principal = principalResult.Value!;
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

        if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrEmpty(jtiClaim))
            return Result<TokenResponseDto>.Unauthorized("Недействительный access token");

        var refreshTokenHash = ITokenService.HashToken(refreshToken);

        var storedToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt =>
                rt.TokenHash == refreshTokenHash &&
                rt.UserId == userId, ct);

        if (storedToken is null)
        {
            _logger.LogWarning("Refresh token не найден для пользователя {UserId}", userId);
            return Result<TokenResponseDto>.Unauthorized("Недействительный refresh token");
        }

        if (storedToken.UsedAt != null || storedToken.RevokedAt != null)
        {
            _logger.LogWarning("Обнаружено повторное использование refresh token! UserId={UserId}, FamilyId={FamilyId}. Отзываем всю семью.",
                userId, storedToken.FamilyId);

            await RevokeTokenFamilyAsync(storedToken.FamilyId, ct);

            return Result<TokenResponseDto>.Unauthorized("Refresh token уже использован. Авторизуйтесь заново.");
        }

        if (storedToken.ExpiresAt <= _appDateTime.UtcNow)
        {
            _logger.LogWarning("Истёкший refresh token для пользователя {UserId}", userId);
            return Result<TokenResponseDto>.Unauthorized("Refresh token истёк");
        }

        if (storedToken.User.IsBanned)
        {
            await RevokeTokenFamilyAsync(storedToken.FamilyId, ct);
            return Result<TokenResponseDto>.Forbidden("Учётная запись заблокирована");
        }

        var role = await DetermineUserRoleAsync(storedToken.User, ct);
        var newTokenPair = _tokenService.GenerateTokenPair(userId, role);

        // Ротация: помечаем старый как использованный
        storedToken.UsedAt = _appDateTime.UtcNow;

        var newRefreshToken = new Model.RefreshToken
        {
            UserId = userId,
            TokenHash = ITokenService.HashToken(newTokenPair.RefreshToken),
            JwtId = newTokenPair.JwtId,
            CreatedAt = _appDateTime.UtcNow,
            ExpiresAt = _appDateTime.UtcNow.AddDays(_jwtSettings.Value.RefreshTokenLifetimeDays),
            FamilyId = storedToken.FamilyId
        };

        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync(ct);

        storedToken.ReplacedByTokenId = newRefreshToken.Id;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Refresh token ротирован для пользователя {UserId}", userId);

        return Result<TokenResponseDto>.Success(new TokenResponseDto
        {
            Token = newTokenPair.AccessToken,
            RefreshToken = newTokenPair.RefreshToken,
            UserId = userId,
            Role = role
        });
    }

    public async Task<Result> RevokeRefreshTokenAsync(int userId, CancellationToken ct = default)
    {
        var now = _appDateTime.UtcNow;

        var revokedCount = await _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, now), ct);

        _logger.LogInformation("Отозвано {Count} refresh tokens для пользователя {UserId}", revokedCount, userId);

        return Result.Success();
    }

    #region Private Methods

    private async Task<Result> SaveRefreshTokenAsync(int userId, TokenPair tokenPair, CancellationToken ct)
    {
        await EnforceSessionLimitAsync(userId, ct);

        var refreshTokenEntity = new Model.RefreshToken
        {
            UserId = userId,
            TokenHash = ITokenService.HashToken(tokenPair.RefreshToken),
            JwtId = tokenPair.JwtId,
            CreatedAt = _appDateTime.UtcNow,
            ExpiresAt = _appDateTime.UtcNow.AddDays(_jwtSettings.Value.RefreshTokenLifetimeDays),
            FamilyId = Guid.NewGuid().ToString()
        };

        _context.RefreshTokens.Add(refreshTokenEntity);

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return saveResult;

        await CleanupExpiredTokensAsync(userId, ct);

        return Result.Success();
    }

    private async Task EnforceSessionLimitAsync(int userId, CancellationToken ct)
    {
        var now = _appDateTime.UtcNow;

        var activeFamilies = await _context.RefreshTokens
            .Where(rt =>
                rt.UserId == userId &&
                rt.RevokedAt == null &&
                rt.UsedAt == null &&
                rt.ExpiresAt > now)
            .GroupBy(rt => rt.FamilyId)
            .Select(g => new
            {
                FamilyId = g.Key,
                LatestCreatedAt = g.Max(rt => rt.CreatedAt)
            })
            .OrderByDescending(f => f.LatestCreatedAt).ToListAsync(ct);

        if (activeFamilies.Count >= MaxActiveSessions)
        {
            var familiesToRevoke = activeFamilies
                .Skip(MaxActiveSessions - 1)
                .Select(f => f.FamilyId).ToList();

            if (familiesToRevoke.Count > 0)
            {
                var revokedCount = await _context.RefreshTokens
                    .Where(rt => familiesToRevoke.Contains(rt.FamilyId) && rt.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, now), ct);

                _logger.LogInformation(
                    "Превышен лимит сессий для пользователя {UserId}: " +
                    "отозвано {RevokedCount} токенов из {FamilyCount} старых сессий " +
                    "(лимит: {MaxSessions})",
                    userId, revokedCount, familiesToRevoke.Count, MaxActiveSessions);
            }
        }
    }

    private async Task RevokeTokenFamilyAsync(string familyId, CancellationToken ct)
    {
        var now = _appDateTime.UtcNow;

        var revokedCount = await _context.RefreshTokens.Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, now), ct);

        _logger.LogWarning("Отозвано {Count} токенов семьи {FamilyId}", revokedCount, familyId);
    }

    private async Task CleanupExpiredTokensAsync(int userId, CancellationToken ct)
    {
        var cutoff = _appDateTime.UtcNow.AddDays(-60);

        var expiredCount = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (expiredCount > 0)
        {
            _logger.LogDebug("Очищено {Count} истёкших refresh tokens для пользователя {UserId}",
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