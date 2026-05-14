using API.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Auth;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _controller = new AuthController(_authServiceMock.Object, NullLogger<AuthController>.Instance);
    }
    [Fact]

    public async Task Login_InternalError_Returns500()
    {
        _authServiceMock.Setup(x => x.LoginAsync("alice", "123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponseDto>.Internal("DB error"));

        var result = await _controller.Login(new LoginRequest("alice", "123"), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;

        objectResult.StatusCode.Should().Be(500);
    }
    [Fact]
    public async Task Login_CallsServiceOnce()
    {
        _authServiceMock.Setup(x => x.LoginAsync("alice", "123", It.IsAny<CancellationToken>())).ReturnsAsync(Result<AuthResponseDto>.Success(new()));

        await _controller.Login(new LoginRequest("alice", "123"), CancellationToken.None);

        _authServiceMock.Verify(x => x.LoginAsync( "alice","123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        var expectedResponse = new AuthResponseDto
        {
            Id = 1,
            Username = "alice",
            DisplayName = "Alice",
            Token = "access-token",
            RefreshToken = "refresh-token",
            Role = UserRole.User
        };

        _authServiceMock.Setup(s => s.LoginAsync("alice", "secret123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponseDto>.Success(expectedResponse));

        var result = await _controller.Login(new LoginRequest("alice", "secret123"), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);

        var body = ok.Value.Should().BeOfType<ApiResponse<AuthResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be("access-token");
        body.Data.Username.Should().Be("alice");
    }

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithNewTokens()
    {
        var expected = new TokenResponseDto
        {
            Token = "new-access",
            RefreshToken = "new-refresh",
            UserId = 1,
            Role = UserRole.User
        };
        _authServiceMock.Setup(s => s.RefreshTokenAsync("old-access", "old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TokenResponseDto>.Success(expected));

        var result = await _controller.Refresh(
            new RefreshTokenRequest("old-access", "old-refresh"), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var body = ok.Value.Should().BeOfType<ApiResponse<TokenResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be("new-access");
        body.Data.RefreshToken.Should().Be("new-refresh");
    }
}