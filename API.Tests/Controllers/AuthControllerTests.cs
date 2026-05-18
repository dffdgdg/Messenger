using API.Configuration;
using API.Controllers;
using API.Services.Core.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Shared.Dto.Auth;
using Shared.Response;
using System.Security.Claims;
using Xunit;

namespace API.Tests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<IOptions<JwtSettings>> _jwtOptionsMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _jwtOptionsMock = new Mock<IOptions<JwtSettings>>();
        _jwtOptionsMock.Setup(o => o.Value).Returns(new JwtSettings
        {
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 30,
            Issuer = "API",
            Audience = "MessengerClient"
        });

        _controller = new AuthController(
            _authServiceMock.Object,
            _jwtOptionsMock.Object,
            NullLogger<AuthController>.Instance);

        // Базовый HttpContext для всех тестов
        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    private void SetupAuthenticatedUser(string userId = "1")
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("sub", userId)
            }, "TestAuth"))
        };

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    private void SetupRefreshTokenCookie(string refreshToken)
    {
        _controller.ControllerContext.HttpContext.Request.Headers
            .Append("Cookie", $"refresh_token={refreshToken}");
    }

    [Fact]
    public async Task Login_InternalError_Returns500()
    {
        _authServiceMock.Setup(x => x.LoginAsync("alice", "123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthLoginResult>.Internal("DB error"));

        var result = await _controller.Login(new LoginRequest("alice", "123"), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        var expectedAuthResponse = new AuthResponseDto
        {
            Id = 1,
            Username = "alice",
            DisplayName = "Alice",
            Token = "access-token",
            Role = UserRole.User
        };

        _authServiceMock.Setup(s => s.LoginAsync("alice", "secret123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthLoginResult>.Success(new AuthLoginResult
            {
                Response = expectedAuthResponse,
                RefreshToken = "refresh-token-string"
            }));

        var result = await _controller.Login(
            new LoginRequest("alice", "secret123"),
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);

        var body = ok.Value.Should().BeOfType<ApiResponse<AuthResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be("access-token");
        body.Data.Username.Should().Be("alice");
        body.Data.Role.Should().Be(UserRole.User);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401()
    {
        _authServiceMock.Setup(s => s.LoginAsync("alice", "wrongpass", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthLoginResult>.Unauthorized("Invalid credentials"));

        var result = await _controller.Login(
            new LoginRequest("alice", "wrongpass"),
            CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Login_UserBanned_Returns403()
    {
        _authServiceMock.Setup(s => s.LoginAsync("banned_user", "pass123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthLoginResult>.Forbidden("User is banned"));

        var result = await _controller.Login(
            new LoginRequest("banned_user", "pass123"),
            CancellationToken.None);

        var forbidden = result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithNewTokens()
    {
        // Arrange
        var expectedTokenResponse = new TokenResponseDto
        {
            Token = "new-access",
            UserId = 1,
            Role = UserRole.User
        };

        _authServiceMock.Setup(s => s.RefreshTokenAsync(
                "old-access",
                "old-refresh",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthRefreshResult>.Success(new AuthRefreshResult
            {
                Response = expectedTokenResponse,
                RefreshToken = "new-refresh"
            }));

        // Добавляем refresh-токен в cookie
        SetupRefreshTokenCookie("old-refresh");

        // Act
        var result = await _controller.Refresh(
            new RefreshTokenRequest("old-access"),
            CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);

        var body = ok.Value.Should().BeOfType<ApiResponse<TokenResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be("new-access");
        body.Data.UserId.Should().Be(1);
        body.Data.Role.Should().Be(UserRole.User);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        _authServiceMock.Setup(s => s.RefreshTokenAsync(
                "expired-access",
                "expired-refresh",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthRefreshResult>.Unauthorized("Token expired"));

        SetupRefreshTokenCookie("expired-refresh");

        var result = await _controller.Refresh(
            new RefreshTokenRequest("expired-access"),
            CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Refresh_TokenReuseDetected_Returns401()
    {
        _authServiceMock.Setup(s => s.RefreshTokenAsync(
                "reused-access",
                "reused-refresh",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthRefreshResult>.Unauthorized("Token reuse detected"));

        SetupRefreshTokenCookie("reused-refresh");

        var result = await _controller.Refresh(
            new RefreshTokenRequest("reused-access"),
            CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Refresh_MissingCookie_Returns401()
    {
        // Не добавляем cookie - должен вернуть 401
        var result = await _controller.Refresh(
            new RefreshTokenRequest("some-access"),
            CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Revoke_ValidRequest_Returns200()
    {
        _authServiceMock.Setup(s => s.RevokeRefreshTokenAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        SetupAuthenticatedUser("1");

        var result = await _controller.Revoke(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Revoke_ServiceError_Returns500()
    {
        _authServiceMock.Setup(s => s.RevokeRefreshTokenAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Internal("Database error"));

        SetupAuthenticatedUser("1");

        var result = await _controller.Revoke(CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(500);
    }
}