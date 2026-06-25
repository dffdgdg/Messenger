using API.Application.Configuration;
using API.Application.Services.Abstractions;
using API.Application.Services.Core.Auth;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Contracts.Auth;
using Shared.Infrastructure;
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

    #region Login Tests

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
            .Returns(Result<AuthLoginResult>.Success(new AuthLoginResult
            {
                Response = expected,
                RefreshToken = ValidRefresh
            }).AsTask());

        var result = await _controller.Login(new LoginRequest(ValidUser, ValidPassword), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<AuthResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be(ValidAccess);
        body.Data.Username.Should().Be(ValidUser);
        body.Data.Role.Should().Be(UserRole.User);
    }

    [Theory]
    [InlineData("Invalid credentials", 401)]
    [InlineData("User is banned", 403)]
    [InlineData("User not found", 404)]
    [InlineData("Session limit reached", 409)]
    [InlineData("DB error", 500)]
    public async Task Login_ServiceError_ReturnsCorrectStatusCode(string errorMessage, int expectedStatus)
    {
        var result = ResultFactory.CreateByStatusCode<AuthLoginResult>(expectedStatus, errorMessage);
        _authServiceMock
            .Setup(s => s.LoginAsync(ValidUser, ValidPassword, It.IsAny<CancellationToken>()))
            .Returns(result.AsTask());
        var actionResult = await _controller.Login(new LoginRequest(ValidUser, ValidPassword), CancellationToken.None);
        actionResult.ShouldHaveStatus(expectedStatus);
    }

    [Theory]
    [InlineData("https", true)]
    [InlineData("http", false)]
    public async Task Login_SetsCookie_WithCorrectSecureFlag(string scheme, bool expectSecure)
    {
        _controller.ControllerContext.HttpContext.Request.Scheme = scheme;

        _authServiceMock
            .Setup(s => s.LoginAsync(ValidUser, ValidPassword, It.IsAny<CancellationToken>()))
            .Returns(Result<AuthLoginResult>.Success(new AuthLoginResult
            {
                Response = new AuthResponseDto
                {
                    Id = 582,
                    Username = ValidUser,
                    DisplayName = "Evelyn Chen",
                    Token = ValidAccess,
                    Role = UserRole.User
                },
                RefreshToken = ValidRefresh
            }).AsTask());

        await _controller.Login(new LoginRequest(ValidUser, ValidPassword), CancellationToken.None);

        var cookieHeader = _controller.Response.Headers.SetCookie.ToString();
        cookieHeader.Should().Contain($"refresh_token={ValidRefresh}");
        cookieHeader.Should().Contain("httponly");
        cookieHeader.Should().Contain("samesite=strict");
        cookieHeader.Should().Contain("path=/api/auth");
        cookieHeader.Should().Contain("expires=");

        if (expectSecure)
            cookieHeader.Should().Contain("secure");
        else
            cookieHeader.Should().NotContain("secure");
    }

    #endregion

    #region Refresh Tests

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithNewTokens()
    {
        const string oldAccess = "eyJhbGciOiJIUzI1NiJ9.old_valid_access";
        const string newAccess = "eyJhbGciOiJIUzI1NiJ9.new_access_token";
        const string newRefresh = "b2c3d4e5-f6a7-8901-bcde-f12345678901";

        _authServiceMock
            .Setup(s => s.RefreshTokenAsync(oldAccess, ValidRefresh, It.IsAny<CancellationToken>()))
            .Returns(Result<AuthRefreshResult>.Success(new AuthRefreshResult
            {
                Response = new TokenResponseDto { Token = newAccess, UserId = 582, Role = UserRole.User },
                RefreshToken = newRefresh
            }).AsTask());

        SetupRefreshTokenCookie(ValidRefresh);

        var result = await _controller.Refresh(new RefreshTokenRequest(oldAccess), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<TokenResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be(newAccess);
        body.Data.UserId.Should().Be(582);
        body.Data.Role.Should().Be(UserRole.User);

        // Verify new cookie set
        var cookieHeader = _controller.Response.Headers.SetCookie.ToString();
        cookieHeader.Should().Contain($"refresh_token={newRefresh}");
    }

    [Theory]
    [InlineData("Token reuse detected")]
    [InlineData("Token expired")]
    public async Task Refresh_InvalidToken_Returns401(string errorMessage)
    {
        _authServiceMock
            .Setup(s => s.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Result<AuthRefreshResult>.Unauthorized(errorMessage).AsTask());

        SetupRefreshTokenCookie(ValidRefresh);

        var result = await _controller.Refresh(new RefreshTokenRequest(ValidAccess), CancellationToken.None);

        result.ShouldBe401();
    }

    [Fact]
    public async Task Refresh_MissingCookie_Returns401()
    {
        var result = await _controller.Refresh(
            new RefreshTokenRequest("eyJhbGciOiJIUzI1NiJ9.some_valid_access"),
            CancellationToken.None);

        result.ShouldBe401();
    }

    [Fact]
    public async Task Refresh_TokenFamilyRevoked_Returns403()
    {
        _authServiceMock
            .Setup(s => s.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Result<AuthRefreshResult>.Forbidden("Token family revoked").AsTask());

        SetupRefreshTokenCookie(ValidRefresh);

        var result = await _controller.Refresh(new RefreshTokenRequest(ValidAccess), CancellationToken.None);

        result.ShouldHaveStatus(403);
    }

    #endregion

    #region Revoke Tests

    [Fact]
    public async Task Revoke_ValidRequest_Returns200AndClearsCookie()
    {
        _authServiceMock
            .Setup(s => s.RevokeRefreshTokenAsync(582, It.IsAny<CancellationToken>()))
            .Returns(Result.Success().AsTask());

        SetupAuthenticatedUser("582");

        var result = await _controller.Revoke(CancellationToken.None);

        result.ShouldHaveStatus(200);

        var cookieHeader = _controller.Response.Headers.SetCookie.ToString();
        cookieHeader.Should().Contain("refresh_token=;");
        cookieHeader.Should().Contain("path=/api/auth");
        cookieHeader.Should().Contain("expires=Thu, 01 Jan 1970");
    }

    [Fact]
    public async Task Revoke_NotFound_Returns404()
    {
        _authServiceMock
            .Setup(s => s.RevokeRefreshTokenAsync(582, It.IsAny<CancellationToken>()))
            .Returns(Result.NotFound("Refresh token not found").AsTask());

        SetupAuthenticatedUser("582");

        var result = await _controller.Revoke(CancellationToken.None);

        result.ShouldHaveStatus(404);
    }

    #endregion
}