using API.Configuration;
using API.Controllers;
using API.Services.Core.Auth;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Dto.Auth;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock = new();
    private readonly AuthController _controller;

    private static readonly JwtSettings JwtSettings = new()
    {
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 30,
        Issuer = "API",
        Audience = "MessengerClient"
    };

    private const string ValidUser = "evelyn.chen";
    private const string ValidPassword = "v8#KpT!2xQzL";
    private const string ValidAccess = "eyJhbGciOiJIUzI1NiJ9.valid_access_token";
    private const string ValidRefresh = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    public AuthControllerTests()
    {
        var jwtOptions = Mock.Of<IOptions<JwtSettings>>(o => o.Value == JwtSettings);

        _controller = new AuthController(
            _authServiceMock.Object,
            jwtOptions,
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private void SetupAuthenticatedUser(string userId = "582") =>
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim("sub", userId)
                ], "TestAuth"))
            }
        };

    private void SetupRefreshTokenCookie(string refreshToken) =>
        _controller.ControllerContext.HttpContext.Request.Headers
            .Append("Cookie", $"refresh_token={refreshToken}");

    // ── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_InternalError_Returns500()
    {
        _authServiceMock
            .Setup(x => x.LoginAsync(ValidUser, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthLoginResult>.Internal("DB error"));

        var result = await _controller.Login(new LoginRequest(ValidUser, ValidPassword), CancellationToken.None);

        result.ShouldHaveStatus(500);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        var expected = new AuthResponseDto
        {
            Id = 582,
            Username = ValidUser,
            DisplayName = "Evelyn Chen",
            Token = ValidAccess,
            Role = UserRole.User
        };

        _authServiceMock
            .Setup(s => s.LoginAsync(ValidUser, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthLoginResult>.Success(new AuthLoginResult
            {
                Response = expected,
                RefreshToken = ValidRefresh
            }));

        var result = await _controller.Login(new LoginRequest(ValidUser, ValidPassword), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<AuthResponseDto>>().Subject;

        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be(ValidAccess);
        body.Data.Username.Should().Be(ValidUser);
        body.Data.Role.Should().Be(UserRole.User);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401()
    {
        _authServiceMock
            .Setup(s => s.LoginAsync(ValidUser, "Wr0ng#P@ssw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthLoginResult>.Unauthorized("Invalid credentials"));

        var result = await _controller.Login(new LoginRequest(ValidUser, "Wr0ng#P@ssw0rd!"), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>()
            .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Login_UserBanned_Returns403()
    {
        _authServiceMock
            .Setup(s => s.LoginAsync(ValidUser, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthLoginResult>.Forbidden("User is banned"));

        var result = await _controller.Login(new LoginRequest(ValidUser, ValidPassword), CancellationToken.None);

        result.ShouldHaveStatus(403);
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithNewTokens()
    {
        const string oldAccess = "eyJhbGciOiJIUzI1NiJ9.old_valid_access";
        const string newAccess = "eyJhbGciOiJIUzI1NiJ9.new_access_token";
        const string newRefresh = "b2c3d4e5-f6a7-8901-bcde-f12345678901";

        _authServiceMock
            .Setup(s => s.RefreshTokenAsync(oldAccess, ValidRefresh, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthRefreshResult>.Success(new AuthRefreshResult
            {
                Response = new TokenResponseDto { Token = newAccess, UserId = 582, Role = UserRole.User },
                RefreshToken = newRefresh
            }));

        SetupRefreshTokenCookie(ValidRefresh);
        var result = await _controller.Refresh(new RefreshTokenRequest(oldAccess), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<TokenResponseDto>>().Subject;

        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be(newAccess);
        body.Data.UserId.Should().Be(582);
        body.Data.Role.Should().Be(UserRole.User);
    }

    [Fact]
    public async Task Refresh_TokenReuseDetected_Returns401()
    {
        const string reusedAccess = "eyJhbGciOiJIUzI1NiJ9.reused_access_token";
        const string reusedRefresh = "f9e8d7c6-b5a4-3210-fedc-ba9876543210";

        _authServiceMock
            .Setup(s => s.RefreshTokenAsync(reusedAccess, reusedRefresh, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthRefreshResult>.Unauthorized("Token reuse detected"));

        SetupRefreshTokenCookie(reusedRefresh);
        var result = await _controller.Refresh(new RefreshTokenRequest(reusedAccess), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>()
            .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Refresh_MissingCookie_Returns401()
    {
        var result = await _controller.Refresh(
            new RefreshTokenRequest("eyJhbGciOiJIUzI1NiJ9.some_valid_access"),
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>()
            .Which.StatusCode.Should().Be(401);
    }

    // ── Revoke ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_ValidRequest_Returns200()
    {
        _authServiceMock
            .Setup(s => s.RevokeRefreshTokenAsync(582, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        SetupAuthenticatedUser("582");
        var result = await _controller.Revoke(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Revoke_ServiceError_Returns500()
    {
        _authServiceMock
            .Setup(s => s.RevokeRefreshTokenAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Internal("Database error"));

        SetupAuthenticatedUser("582");
        var result = await _controller.Revoke(CancellationToken.None);

        result.ShouldHaveStatus(500);
    }
}