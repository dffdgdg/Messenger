using Messenger.Tests.Helpers;
using MessengerAPI.Services.User;
using MessengerShared.DTO.User;

namespace Messenger.Tests.Services;

public class AdminServiceTests
{
    [Fact]
    public async Task CreateUser_Success_WhenValidData()
    {
        await using var context = await TestHelpers.CreateSeededDbContext();
        var service = new AdminService(context, TestHelpers.CreateLogger<AdminService>().Object);

        var dto = new CreateUserDTO
        {
            Username = "newuser",
            Password = "password123",
            Name = "New",
            Surname = "User",
            DepartmentId = 1
        };

        var result = await service.CreateUserAsync(dto);

        result.Should().NotBeNull();
        result.Username.Should().Be("newuser");

        var userInDb = await context.Users.FindAsync(result.Id);
        userInDb.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateUser_Failure_WhenUsernameExists()
    {
        await using var context = await TestHelpers.CreateSeededDbContext();
        var service = new AdminService(context, TestHelpers.CreateLogger<AdminService>().Object);
        var dto = new CreateUserDTO
        {
            Username = "user1",
            Password = "password123",
            Name = "Test",
            Surname = "User"
        };
        var act = () => service.CreateUserAsync(dto);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*уже существует*");
    }

    [Theory]
    [InlineData("", "Логин не может быть пустым")]
    [InlineData("ab", "3-30 символов")]
    [InlineData("user@!", "3-30 символов")]
    public async Task CreateUser_Failure_WhenInvalidUsername(string username, string expectedError)
    {
        // Arrange
        await using var context = TestHelpers.CreateDbContext();
        var service = new AdminService(context, TestHelpers.CreateLogger<AdminService>().Object);

        var dto = new CreateUserDTO
        {
            Username = username,
            Password = "password123",
            Name = "Test",
            Surname = "User"
        };

        // Act & Assert
        var act = () => service.CreateUserAsync(dto);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{expectedError}*");
    }

    [Fact]
    public async Task CreateUser_Failure_WhenPasswordTooShort()
    {
        await using var context = TestHelpers.CreateDbContext();
        var service = new AdminService(context, TestHelpers.CreateLogger<AdminService>().Object);

        var dto = new CreateUserDTO
        {
            Username = "validuser",
            Password = "123", 
            Name = "Test",
            Surname = "User"
        };
        var act = () => service.CreateUserAsync(dto);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*минимум 6*");
    }

    [Fact]
    public async Task ToggleBan_Success()
    {
        // Arrange
        await using var context = await TestHelpers.CreateSeededDbContext();
        var service = new AdminService(context, TestHelpers.CreateLogger<AdminService>().Object);
        await service.ToggleBanAsync(2);
        var user = await context.Users.FindAsync(2);
        user!.IsBanned.Should().BeTrue();
        await service.ToggleBanAsync(2);
        user = await context.Users.FindAsync(2);
        user!.IsBanned.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleBan_Failure_WhenUserNotExists()
    {
        // Arrange
        await using var context = TestHelpers.CreateDbContext();
        var service = new AdminService(context, TestHelpers.CreateLogger<AdminService>().Object);

        // Act & Assert
        var act = () => service.ToggleBanAsync(999);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}