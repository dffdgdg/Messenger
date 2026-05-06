using API.Services.Base;
using API.Services.Infrastructure.Bundles;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace API.Services.Auth;

public sealed partial class AuthService : BaseService<AuthService>, IAuthService
{
    private readonly ITokenService _tokenService;
    private readonly MessengerSettings _settings;
    private readonly AppDateTime _appDateTime;
    private readonly IOptions<JwtSettings> _jwtSettings;
    private readonly string _dummyHash;
    private const int MaxActiveSessions = 5;

    public AuthService(MessengerDbContext context, ITokenService tokenService, IOptions<MessengerSettings> settings,
        IOptions<JwtSettings> jwtSettings, TimeBundle time, ILogger<AuthService> logger) : base(context, logger)
    {
        _tokenService = tokenService;
        _settings = settings.Value;
        _jwtSettings = jwtSettings;
        _appDateTime = time.AppDateTime;
        _dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password", _settings.BcryptWorkFactor);
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Result<AuthResponseDto>.Unauthorized("Неверное имя пользователя или пароль");

        var user = await _context.Users.Include(u => u.Password).AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);

        if (user is null)
        {
            BCrypt.Net.BCrypt.Verify(password, _dummyHash);
            return Result<AuthResponseDto>.Unauthorized("Неверное имя пользователя или пароль");
        }

        if (!user.Password.Verify(password))
        {
            LogFailedLogin(username);
            return Result<AuthResponseDto>.Unauthorized("Неверное имя пользователя или пароль");
        }

        if (user.IsBanned)
        {
            LogBannedLogin(username);
            return Result<AuthResponseDto>.Forbidden("Учётная запись заблокирована");
        }

        var role = await DetermineUserRoleAsync(user, ct);
        var tokenPair = _tokenService.GenerateTokenPair(user.Id, role);

        var saveResult = await SaveRefreshTokenAsync(user.Id, tokenPair, ct);
        if (saveResult.IsFailure)
            return saveResult.As<AuthResponseDto>();

        var response = new AuthResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.GetDisplayName(),
            Token = tokenPair.AccessToken,
            RefreshToken = tokenPair.RefreshToken,
            Role = role
        };

        LogSuccessfulLogin(username, role);

        return Result<AuthResponseDto>.Success(response);
    }

    public async Task<Result<TokenResponseDto>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default)
    {
        var principalResult = _tokenService.GetPrincipalFromExpiredToken(accessToken);

        if (principalResult.IsFailure)
            return principalResult.As<TokenResponseDto>();

        var principal = principalResult.Value!;
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

        if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrEmpty(jtiClaim))
            return Result<TokenResponseDto>.Unauthorized("Недействительный access token");

        var refreshTokenHash = ITokenService.HashToken(refreshToken);

        var storedToken = await _context.RefreshTokens.Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == refreshTokenHash && rt.UserId == userId, ct);

        if (storedToken is null)
        {
            LogRefreshTokenNotFound(userId);
            return Result<TokenResponseDto>.Unauthorized("Недействительный refresh token");
        }

        if (storedToken.UsedAt != null || storedToken.RevokedAt != null)
        {
            LogTokenReuse(userId, storedToken.FamilyId);
            await RevokeTokenFamilyAsync(storedToken.FamilyId, ct);
            return Result<TokenResponseDto>.Unauthorized("Refresh token уже использован. Авторизуйтесь заново.");
        }

        if (storedToken.ExpiresAt <= _appDateTime.UtcNow)
        {
            LogExpiredRefreshToken(userId);
            return Result<TokenResponseDto>.Unauthorized("Refresh token истёк");
        }

        if (storedToken.User.IsBanned)
        {
            await RevokeTokenFamilyAsync(storedToken.FamilyId, ct);
            return Result<TokenResponseDto>.Forbidden("Учётная запись заблокирована");
        }

        var role = await DetermineUserRoleAsync(storedToken.User, ct);
        var newTokenPair = _tokenService.GenerateTokenPair(userId, role);

        storedToken.UsedAt = _appDateTime.UtcNow;

        var newRefreshToken = new RefreshToken
        {
            UserId = userId,
            TokenHash = ITokenService.HashToken(newTokenPair.RefreshToken),
            JwtId = newTokenPair.JwtId,
            CreatedAt = _appDateTime.UtcNow,
            ExpiresAt = _appDateTime.UtcNow.AddDays(_jwtSettings.Value.RefreshTokenLifetimeDays),
            FamilyId = storedToken.FamilyId
        };

        storedToken.ReplacedByToken = newRefreshToken;

        _context.RefreshTokens.Add(newRefreshToken);

        await _context.SaveChangesAsync(ct);

        LogTokenRotated(userId);

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
        var revokedCount = await _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, _appDateTime.UtcNow), ct);

        LogTokensRevoked(revokedCount, userId);

        return Result.Success();
    }

    #region Private Methods

    private async Task<Result> SaveRefreshTokenAsync(int userId, TokenPair tokenPair, CancellationToken ct)
    {
        await EnforceSessionLimitAsync(userId, ct);

        var refreshTokenEntity = new RefreshToken
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

        var activeFamilies = await _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.UsedAt == null && rt.ExpiresAt > now)
            .GroupBy(rt => rt.FamilyId).Select(g => new { FamilyId = g.Key, LatestCreatedAt = g.Max(rt => rt.CreatedAt) }).OrderByDescending(f => f.LatestCreatedAt).ToListAsync(ct);

        if (activeFamilies.Count >= MaxActiveSessions)
        {
            var familiesToRevoke = activeFamilies.Skip(MaxActiveSessions - 1).Select(f => f.FamilyId).ToList();

            if (familiesToRevoke.Count > 0)
            {
                var revokedCount = await _context.RefreshTokens.Where(rt => familiesToRevoke.Contains(rt.FamilyId) && rt.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, now), ct);
                LogSessionLimitExceeded(userId, revokedCount, familiesToRevoke.Count, MaxActiveSessions);
            }
        }
    }

    private async Task RevokeTokenFamilyAsync(string familyId, CancellationToken ct)
    {
        var revokedCount = await _context.RefreshTokens.Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, _appDateTime.UtcNow), ct);

        LogFamilyRevoked(revokedCount, familyId);
    }

    private async Task CleanupExpiredTokensAsync(int userId, CancellationToken ct)
    {
        var expiredCount = await _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.ExpiresAt < _appDateTime.UtcNow.AddDays(-60)).ExecuteDeleteAsync(ct);

        if (expiredCount > 0)
        {
            LogExpiredTokensCleanup(expiredCount, userId);
        }
    }

    private async Task<UserRole> DetermineUserRoleAsync(Data.User user, CancellationToken ct)
    {
        if (user.DepartmentId == _settings.AdminDepartmentId)
            return UserRole.Admin;

        var isHead = await _context.Departments.AnyAsync(d => d.HeadId == user.Id, ct);

        return isHead ? UserRole.Head : UserRole.User;
    }

    #endregion

    #region Log

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Неудачная попытка входа: {Username}")]
    private partial void LogFailedLogin(string username);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Попытка входа заблокированного пользователя: {Username}")]
    private partial void LogBannedLogin(string username);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Успешный вход: {Username} (роль: {Role})")]
    private partial void LogSuccessfulLogin(string username, UserRole role);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Refresh token не найден для пользователя {UserId}")]
    private partial void LogRefreshTokenNotFound(int userId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Обнаружено повторное использование refresh token! UserId={UserId}, FamilyId={FamilyId}. Отзываем всю семью.")]
    private partial void LogTokenReuse(int userId, string familyId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Истёкший refresh token для пользователя {UserId}")]
    private partial void LogExpiredRefreshToken(int userId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Refresh token ротирован для пользователя {UserId}")]
    private partial void LogTokenRotated(int userId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Отозвано {Count} refresh tokens для пользователя {UserId}")]
    private partial void LogTokensRevoked(int count, int userId);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Превышен лимит сессий для пользователя {UserId}: отозвано {RevokedCount} токенов из {FamilyCount} старых сессий (лимит: {MaxSessions})")]
    private partial void LogSessionLimitExceeded(int userId, int revokedCount, int familyCount, int maxSessions);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Отозвано {Count} токенов семьи {FamilyId}")]
    private partial void LogFamilyRevoked(int count, string familyId);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "Очищено {Count} истёкших refresh tokens для пользователя {UserId}")]
    private partial void LogExpiredTokensCleanup(int count, int userId);

    #endregion
}