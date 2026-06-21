using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using API.Common.Patterns;
using API.Configuration;
using API.Services.Core.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Shared.Enum;
using Xunit;

namespace API.Tests.Services;

public class TokenServiceTests
{
    private readonly TokenService _service;
    private static readonly JwtSettings Settings = new()
    {
        Secret = "super-secret-key-with-at-least-32-characters-long!!",
        Issuer = "API",
        Audience = "MessengerClient",
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 30
    };

    public TokenServiceTests()
    {
        var options = Mock.Of<IOptions<JwtSettings>>(o => o.Value == Settings);
        _service = new TokenService(options);
    }

    [Fact]
    public void Constructor_ShortSecret_Throws()
    {
        var shortSettings = new JwtSettings { Secret = "short" };
        var opts = Mock.Of<IOptions<JwtSettings>>(o => o.Value == shortSettings);

        Action act = () => new TokenService(opts);
        act.Should().Throw<InvalidOperationException>().WithMessage("*32*");
    }

    [Fact]
    public void Constructor_PlaceholderSecret_Throws()
    {
        var placeholderSettings = new JwtSettings { Secret = "CHANGE-ME-CONFIGURE-A-REAL-SECRET" };
        var opts = Mock.Of<IOptions<JwtSettings>>(o => o.Value == placeholderSettings);

        Action act = () => new TokenService(opts);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not configured*");
    }

    [Fact]
    public void Constructor_EmptySecret_Throws()
    {
        var emptySettings = new JwtSettings { Secret = "" };
        var opts = Mock.Of<IOptions<JwtSettings>>(o => o.Value == emptySettings);

        Action act = () => new TokenService(opts);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GenerateTokenPair_ReturnsValidTokens()
    {
        var pair = _service.GenerateTokenPair(582);

        pair.Should().NotBeNull();
        pair.AccessToken.Should().NotBeNullOrEmpty();
        pair.RefreshToken.Should().NotBeNullOrEmpty();
        pair.JwtId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateTokenPair_RefreshToken_IsBase64()
    {
        var pair = _service.GenerateTokenPair(582);

        var refreshBytes = Convert.FromBase64String(pair.RefreshToken);
        refreshBytes.Length.Should().Be(64);
    }

    [Fact]
    public void GenerateTokenPair_WithRole_IncludesRoleClaim()
    {
        var pair = _service.GenerateTokenPair(582, UserRole.Admin);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(pair.AccessToken);
        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == "role");
        roleClaim.Should().NotBeNull();
        roleClaim!.Value.Should().Be("2");
    }

    [Fact]
    public void GenerateTokenPair_WithoutRole_NoRoleClaim()
    {
        var pair = _service.GenerateTokenPair(582);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(pair.AccessToken);
        jwt.Claims.Should().NotContain(c => c.Type == "role");
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsTrueAndUserId()
    {
        var pair = _service.GenerateTokenPair(42);

        var result = _service.ValidateToken(pair.AccessToken, out var userId);

        result.Should().BeTrue();
        userId.Should().Be(42);
    }

    [Fact]
    public void ValidateToken_EmptyToken_ReturnsFalse()
    {
        var result = _service.ValidateToken("", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_NullToken_ReturnsFalse()
    {
        var result = _service.ValidateToken(null!, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsFalse()
    {
        var result = _service.ValidateToken("invalid.token.here", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_DifferentKey_ReturnsFalse()
    {
        var otherSettings = new JwtSettings
        {
            Secret = "different-secret-key-with-32-chars!!!",
            Issuer = "API",
            Audience = "MessengerClient",
            AccessTokenLifetimeMinutes = 15
        };
        var otherService = new TokenService(Mock.Of<IOptions<JwtSettings>>(o => o.Value == otherSettings));
        var pair = _service.GenerateTokenPair(100);

        var result = otherService.ValidateToken(pair.AccessToken, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_ValidExpired_ReturnsPrincipal()
    {
        // Создаём токен с истекшим сроком вручную
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(Settings.Secret));
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "42")]),
            Expires = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Issuer = Settings.Issuer,
            Audience = Settings.Audience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        var token = handler.CreateToken(tokenDescriptor);
        var expiredToken = handler.WriteToken(token);

        var result = _service.GetPrincipalFromExpiredToken(expiredToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("42");
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_EmptyToken_ReturnsUnauthorized()
    {
        var result = _service.GetPrincipalFromExpiredToken("");

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_NullToken_ReturnsUnauthorized()
    {
        var result = _service.GetPrincipalFromExpiredToken(null!);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_InvalidToken_ReturnsUnauthorized()
    {
        var result = _service.GetPrincipalFromExpiredToken("garbage");

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public void GetValidationParameters_ReturnsConfiguredParameters()
    {
        var parameters = _service.GetValidationParameters();

        parameters.ValidateIssuer.Should().BeTrue();
        parameters.ValidIssuer.Should().Be("API");
        parameters.ValidateAudience.Should().BeTrue();
        parameters.ValidAudience.Should().Be("MessengerClient");
        parameters.ValidateLifetime.Should().BeTrue();
        parameters.ClockSkew.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void HashToken_ProducesConsistentHash()
    {
        var hash1 = ITokenService.HashToken("test_token");
        var hash2 = ITokenService.HashToken("test_token");

        hash1.Should().Be(hash2);
        hash1.Should().NotBe("test_token");
    }

    [Fact]
    public void Constructor_ValidSecret_DoesNotThrow()
    {
        Action act = () => new TokenService(Mock.Of<IOptions<JwtSettings>>(o => o.Value == Settings));
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateTokenPair_IncludesJtiClaim()
    {
        var pair = _service.GenerateTokenPair(582);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(pair.AccessToken);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public void GenerateTokenPair_IncludesIatClaim()
    {
        var pair = _service.GenerateTokenPair(582);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(pair.AccessToken);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Iat);
    }
}