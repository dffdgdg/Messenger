using Messenger.Tests.Helpers;
using MessengerAPI.Configuration;
using MessengerAPI.Helpers;
using Microsoft.Extensions.Options;

namespace Messenger.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly IOptions<MessengerSettings> _settings;

    public AuthServiceTests()
    {
        _tokenServiceMock = new Mock<ITokenService>();
        _tokenServiceMock.Setup(t => t.GenerateToken(It.IsAny<int>(), It.IsAny<UserRole>())).Returns("test-token");

        _settings = Options.Create(new MessengerSettings
        {
            AdminDepartmentId = 2,
            BcryptWorkFactor = 10
        });
    }

    [Fact]
    public async Task Login_Success_WhenValidCredentials()
    {
        await using var context = await TestHelpers.CreateSeededDbContext();
        var service = new AuthService(context, _tokenServiceMock.Object, _settings,
            TestHelpers.CreateLogger<AuthService>().Object);

        var result = await service.LoginAsync("user1", "user123");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Username.Should().Be("user1");
        result.Value.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_Failure_WhenWrongPassword()
    {
        await using var context = await TestHelpers.CreateSeededDbContext();
        var service = new AuthService(context, _tokenServiceMock.Object, _settings,
            TestHelpers.CreateLogger<AuthService>().Object);

        var result = await service.LoginAsync("user1", "wrongpassword");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Неверное");
    }

    [Fact]
    public async Task Login_Failure_WhenUserNotExists()
    {
        // Arrange
        await using var context = await TestHelpers.CreateSeededDbContext();
        var service = new AuthService(context, _tokenServiceMock.Object, _settings,TestHelpers.CreateLogger<AuthService>().Object);

        // Act
        var result = await service.LoginAsync("nonexistent", "password");

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Login_ReturnsAdminRole_WhenUserInAdminDepartment()
    {
        await using var context = await TestHelpers.CreateSeededDbContext();
        var service = new AuthService(context, _tokenServiceMock.Object, _settings,
            TestHelpers.CreateLogger<AuthService>().Object);

        var result = await service.LoginAsync("admin", "admin123");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Role.Should().Be(UserRole.Admin);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("user", "")]
    [InlineData(null, "password")]
    [InlineData("user", null)]
    public async Task Login_Failure_WhenEmptyCredentials(string? username, string? password)
    {
        // Arrange
        await using var context = TestHelpers.CreateDbContext();
        var service = new AuthService(context, _tokenServiceMock.Object, _settings,TestHelpers.CreateLogger<AuthService>().Object);

        // Act
        var result = await service.LoginAsync(username!, password!);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }
}