using API.Application.Bundles;
using API.Application.Configuration;
using API.Application.Services.Abstractions;
using API.Application.Services.Core.Auth;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Tests.Infrastructure.TestFixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Enum;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace API.Tests.Services;

public class AuthServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<ITokenService> _tokenMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRefreshTokenRepository> _tokenRepoMock = new();
    private readonly Mock<IDepartmentRepository> _deptRepoMock = new();
    private readonly AuthService _service;

    private static readonly JwtSettings JwtSettings = new()
    {
        Secret = "test-secret-with-32-chars-long!!",
        Issuer = "API",
        Audience = "MessengerClient",
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 30
    };

    private static readonly MessengerSettings MessengerSettings = new()
    {
        BcryptWorkFactor = 4,
        AdminDepartmentId = 1
    };

    public AuthServiceTests()
    {
        _context = DbContextFactory.Create();
        _unitOfWork = new UnitOfWork(_context, NullLogger<UnitOfWork>.Instance);

        _service = new AuthService(
            _unitOfWork,
            _deptRepoMock.Object,
            _tokenMock.Object,
            Options.Create(MessengerSettings),
            Options.Create(JwtSettings),
            new TimeBundle(new AppDateTime(TimeProvider.System)),
            _userRepoMock.Object,
            _tokenRepoMock.Object,
            NullLogger<AuthService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task Login_EmptyUsername_ReturnsUnauthorized()
    {
        var result = await _service.LoginAsync("", "pass");
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public async Task Login_UserNotFound_ReturnsUnauthorized()
    {
        _userRepoMock.Setup(r => r.FindByUsernameAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.LoginAsync("unknown", "pass");
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var user = CreateUser("testuser", "correct_pass");
        _userRepoMock.Setup(r => r.FindByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _service.LoginAsync("testuser", "wrong_pass");
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public async Task Login_UserBanned_ReturnsForbidden()
    {
        var user = CreateUser("banned", "pass", isBanned: true);
        _userRepoMock.Setup(r => r.FindByUsernameAsync("banned", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _service.LoginAsync("banned", "pass");
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokens()
    {
        var user = CreateUser("valid", "pass123", departmentId: 2);
        _userRepoMock.Setup(r => r.FindByUsernameAsync("valid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _deptRepoMock.Setup(r => r.IsHeadOfAnyDepartmentAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tokenMock.Setup(t => t.GenerateTokenPair(user.Id, It.IsAny<UserRole>()))
            .Returns(new TokenPair { AccessToken = "access_token", RefreshToken = "refresh_token", JwtId = "jti-123" });
        _tokenRepoMock.Setup(t => t.GetActiveFamiliesAsync(user.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.LoginAsync("valid", "pass123");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Response.Token.Should().Be("access_token");
        result.Value.RefreshToken.Should().Be("refresh_token");
    }

    [Fact]
    public async Task Refresh_InvalidAccessToken_ReturnsUnauthorized()
    {
        _tokenMock.Setup(t => t.GetPrincipalFromExpiredToken("bad_token"))
            .Returns(Result<ClaimsPrincipal>.Unauthorized("bad"));

        var result = await _service.RefreshTokenAsync("bad_token", "refresh");
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public async Task Refresh_StoredTokenNotFound_ReturnsUnauthorized()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(JwtRegisteredClaimNames.Jti, "jti-1")
        ]));
        _tokenMock.Setup(t => t.GetPrincipalFromExpiredToken("expired"))
            .Returns(Result<ClaimsPrincipal>.Success(principal));
        _tokenRepoMock.Setup(t => t.FindByHashWithUserAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        var result = await _service.RefreshTokenAsync("expired", "refresh");
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public async Task Revoke_CallsRepository()
    {
        _tokenRepoMock.Setup(t => t.RevokeAllForUserAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await _service.RevokeRefreshTokenAsync(1);
        result.IsSuccess.Should().BeTrue();
        _tokenRepoMock.Verify(t => t.RevokeAllForUserAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static User CreateUser(string username, string password, bool isBanned = false, int? departmentId = null)
    {
        var user = new User
        {
            Id = Math.Abs(username.GetHashCode()) % 10000,
            Username = username,
            IsBanned = isBanned,
            DepartmentId = departmentId,
            Password = new UserPassword()
        };
        user.Password.SetPassword(password);
        return user;
    }

    [Fact]
    public async Task Refresh_ExpiredRefreshToken_ReturnsUnauthorized()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(JwtRegisteredClaimNames.Jti, "jti-1")
        ]));
        _tokenMock.Setup(t => t.GetPrincipalFromExpiredToken("expired_access"))
            .Returns(Result<ClaimsPrincipal>.Success(principal));

        var storedToken = new RefreshToken
        {
            UserId = 1,
            TokenHash = _tokenMock.Object.HashToken("refresh_token"),
            JwtId = "jti-1",
            FamilyId = "fam-1",
            ExpiresAt = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            User = new User { Id = 1, Username = "test", IsBanned = false }
        };
        _tokenRepoMock.Setup(t => t.FindByHashWithUserAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        var result = await _service.RefreshTokenAsync("expired_access", "refresh_token");

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
        result.Error.Should().Contain("истёк");
    }

    [Fact]
    public async Task Refresh_UserBanned_ReturnsForbidden()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(JwtRegisteredClaimNames.Jti, "jti-1")
        ]));
        _tokenMock.Setup(t => t.GetPrincipalFromExpiredToken("expired_access"))
            .Returns(Result<ClaimsPrincipal>.Success(principal));

        var storedToken = new RefreshToken
        {
            UserId = 1,
            TokenHash = _tokenMock.Object.HashToken("refresh_token"),
            JwtId = "jti-1",
            FamilyId = "fam-1",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            User = new User { Id = 1, Username = "test", IsBanned = true }
        };
        _tokenRepoMock.Setup(t => t.FindByHashWithUserAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        var result = await _service.RefreshTokenAsync("expired_access", "refresh_token");

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task Refresh_Success_RotatesToken()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(JwtRegisteredClaimNames.Jti, "jti-1")
        ]));
        _tokenMock.Setup(t => t.GetPrincipalFromExpiredToken("expired_access"))
            .Returns(Result<ClaimsPrincipal>.Success(principal));

        var storedToken = new RefreshToken
        {
            UserId = 1,
            TokenHash = _tokenMock.Object.HashToken("old_refresh"),
            JwtId = "jti-1",
            FamilyId = "fam-1",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            User = new User { Id = 1, Username = "test", IsBanned = false }
        };
        _tokenRepoMock.Setup(t => t.FindByHashWithUserAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _deptRepoMock.Setup(r => r.IsHeadOfAnyDepartmentAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tokenMock.Setup(t => t.GenerateTokenPair(1, It.IsAny<UserRole>()))
            .Returns(new TokenPair { AccessToken = "new_access", RefreshToken = "new_refresh", JwtId = "jti-2" });

        var result = await _service.RefreshTokenAsync("expired_access", "old_refresh");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Response.Token.Should().Be("new_access");
        result.Value.RefreshToken.Should().Be("new_refresh");
        storedToken.UsedAt.Should().NotBeNull();
    }
}