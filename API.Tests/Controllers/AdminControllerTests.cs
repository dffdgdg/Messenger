using API.Common.Patterns;
using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.User;
using Xunit;

namespace API.Tests.Controllers;

public class AdminControllerTests
{
    private readonly Mock<IAdminService> _adminMock = new();
    private readonly AdminController _controller;

    public AdminControllerTests()
    {
        _controller = new AdminController(_adminMock.Object, NullLogger<AdminController>.Instance);
        AuthHelper.SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task GetUsers_ReturnsSuccess()
    {
        _adminMock.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<UserDto>>.Success([]));
        var result = await _controller.GetUsers(CancellationToken.None);
        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task CreateUser_ReturnsSuccess()
    {
        var dto = new CreateUserDto { Username = "newuser" };
        var expected = new UserDto { Id = 42, Username = "newuser" };
        _adminMock.Setup(s => s.CreateUserAsync(dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Success(expected));

        var data = (await _controller.CreateUser(dto, CancellationToken.None))
            .ShouldHaveSuccessBody<UserDto>();
        data.Username.Should().Be("newuser");
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_Returns409()
    {
        var dto = new CreateUserDto { Username = "duplicate" };
        _adminMock.Setup(s => s.CreateUserAsync(dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Conflict("Username already exists"));
        var result = await _controller.CreateUser(dto, CancellationToken.None);
        result.ShouldHaveStatus(409);
    }

    [Fact]
    public async Task UpdateUser_ReturnsSuccess()
    {
        var dto = new UserDto { Id = 42 };
        _adminMock.Setup(s => s.UpdateUserAsync(42, dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Success(new UserDto()));
        var result = await _controller.UpdateUser(42, dto, CancellationToken.None);
        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task UpdateUser_NotFound_Returns404()
    {
        _adminMock.Setup(s => s.UpdateUserAsync(999, It.IsAny<UserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.NotFound("User not found"));
        var result = await _controller.UpdateUser(999, new UserDto(), CancellationToken.None);
        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task ToggleBan_ReturnsSuccess()
    {
        _adminMock.Setup(s => s.ToggleBanAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Success(new UserDto { IsBanned = true }));
        var result = await _controller.ToggleBan(42, CancellationToken.None);
        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ToggleBan_NotFound_Returns404()
    {
        _adminMock.Setup(s => s.ToggleBanAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.NotFound("User not found"));
        var result = await _controller.ToggleBan(999, CancellationToken.None);
        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task ResetPassword_ReturnsSuccess()
    {
        var dto = new ResetPasswordAdminDto { NewPassword = "newSecureP@ss1" };
        _adminMock.Setup(s => s.ResetPasswordAsync(42, "newSecureP@ss1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var result = await _controller.ResetPassword(42, dto, CancellationToken.None);
        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ResetPassword_WeakPassword_Returns400()
    {
        var dto = new ResetPasswordAdminDto { NewPassword = "123" };
        _adminMock.Setup(s => s.ResetPasswordAsync(42, "123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Слишком короткий пароль"));
        var result = await _controller.ResetPassword(42, dto, CancellationToken.None);
        result.ShouldHaveStatus(400);
    }
}