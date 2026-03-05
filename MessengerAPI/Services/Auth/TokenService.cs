using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MessengerAPI.Services.Auth;

public sealed class TokenPair
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required string JwtId { get; init; }
}

public interface ITokenService
{
    /// <summary>
    /// Генерирует пару access + refresh токенов.
    /// </summary>
    TokenPair GenerateTokenPair(int userId, UserRole? role = null);

    /// <summary>
    /// Валидирует access token (включая проверку срока действия).
    /// </summary>
    bool ValidateToken(string token, out int userId);

    /// <summary>
    /// Извлекает claims из истёкшего access token без проверки срока действия.
    /// Используется при refresh.
    /// </summary>
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);

    /// <summary>
    /// Хеширует refresh token для хранения в БД.
    /// </summary>
    static string HashToken(string token) => Convert.ToBase64String(
        SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    TokenValidationParameters GetValidationParameters();
}

public sealed class TokenService : ITokenService
{
    private const string SecretPlaceholder = "CHANGE-ME-CONFIGURE-A-REAL-SECRET";
    private readonly JwtSettings _settings;
    private readonly SymmetricSecurityKey _signingKey;

    public TokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;

        if (string.IsNullOrWhiteSpace(_settings.Secret)
            || _settings.Secret.StartsWith("CHANGE-ME", StringComparison.OrdinalIgnoreCase)
            || _settings.Secret == SecretPlaceholder)
        {
            throw new InvalidOperationException(
                "JWT Secret is not configured. Set 'Jwt:Secret' via environment " +
                "variable or user-secrets. Do NOT commit secrets to source control.");
        }

        if (_settings.Secret.Length < 32)
            throw new InvalidOperationException("JWT Secret must be at least 32 characters");

        _signingKey = CreateSigningKey(_settings.Secret);
    }

    public TokenPair GenerateTokenPair(int userId, UserRole? role = null)
    {
        var jti = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        if (role.HasValue)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Value.ToString()));
        }

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_settings.AccessTokenLifetimeMinutes),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            SigningCredentials = credentials,
            NotBefore = DateTime.UtcNow
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(token);

        var refreshToken = GenerateRefreshToken();

        return new TokenPair
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            JwtId = jti
        };
    }

    public bool ValidateToken(string token, out int userId)
    {
        userId = 0;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, GetValidationParameters(), out _);

            var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
            return int.TryParse(idClaim?.Value, out userId);
        }
        catch
        {
            return false;
        }
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = true,
                ValidAudience = _settings.Audience,
                ValidateLifetime = false, // Ключевое: НЕ проверяем срок действия
                ClockSkew = TimeSpan.Zero
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParameters, out var securityToken);

            if (securityToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }

    public TokenValidationParameters GetValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    }

    public static TokenValidationParameters CreateValidationParameters(IConfiguration config)
    {
        var keyMaterial = config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(keyMaterial)
            || keyMaterial.StartsWith("CHANGE-ME", StringComparison.OrdinalIgnoreCase)
            || keyMaterial == SecretPlaceholder)
        {
            throw new InvalidOperationException(
                "Jwt:Secret configuration is required. Set via environment " +
                "variable or user-secrets. Do NOT commit secrets to source control.");
        }

        if (keyMaterial.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters");

        var issuer = config["Jwt:Issuer"] ?? "MessengerAPI";
        var audience = config["Jwt:Audience"] ?? "MessengerClient";

        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = CreateSigningKey(keyMaterial),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }

    private static SymmetricSecurityKey CreateSigningKey(string keyMaterial)
        => new(Encoding.UTF8.GetBytes(keyMaterial));
}