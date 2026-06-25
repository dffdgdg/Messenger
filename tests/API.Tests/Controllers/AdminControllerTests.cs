using API.Application.Common;
using API.Application.Features.Admin;
using API.Application.Features.Admin.Commands;
using API.Application.Features.Admin.Queries;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.User;
using Xunit;

namespace API.Tests.Controllers;

public class AdminControllerTests : ControllerTestBase
{
    private readonly Mock<IAdminHandlers> _handlers = new();
    private readonly Mock<ICommandHandler<CreateUserCommand, UserDto>> _createUser = new();
    private readonly Mock<ICommandHandler<UpdateUserCommand, UserDto>> _updateUser = new();
    private readonly Mock<ICommandHandler<ToggleBanCommand>> _toggleBan = new();
    private readonly Mock<ICommandHandler<ResetPasswordCommand>> _resetPassword = new();
    private readonly Mock<IQueryHandler<GetUsersQuery, Result<List<UserDto>>>> _getUsers = new();
    private readonly AdminController _controller;

    public AdminControllerTests()
    {
        _handlers.Setup(h => h.CreateUser).Returns(_createUser.Object);
        _handlers.Setup(h => h.UpdateUser).Returns(_updateUser.Object);
        _handlers.Setup(h => h.ToggleBan).Returns(_toggleBan.Object);
        _handlers.Setup(h => h.ResetPassword).Returns(_resetPassword.Object);
        _handlers.Setup(h => h.GetUsers).Returns(_getUsers.Object);

        _controller = new AdminController(_handlers.Object, NullLogger<AdminController>.Instance);
        SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task GetUsers_ReturnsSuccess()
    {
        _getUsers
            .Setup(h => h.HandleAsync(It.IsAny<GetUsersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<UserDto>>.Success([]));

        var result = await _controller.GetUsers(CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task CreateUser_ReturnsSuccess()
    {
        _createUser
            .Setup(h => h.HandleAsync(
                It.Is<CreateUserCommand>(c => c.Dto.Username == "newuser"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Success(new UserDto { Id = 42, Username = "newuser" }));

        var result = await _controller.CreateUser(
            new CreateUserDto { Username = "newuser", Password = "StrongP@ss1", Surname = "Test", Name = "User" },
            CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_Returns409()
    {
        _createUser
            .Setup(h => h.HandleAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Conflict("Username already exists"));

        var result = await _controller.CreateUser(
            new CreateUserDto { Username = "duplicate", Password = "StrongP@ss1", Surname = "Test", Name = "User" },
            CancellationToken.None);

        result.ShouldHaveStatus(409);
    }

    [Fact]
    public async Task UpdateUser_ReturnsSuccess()
    {
        _updateUser
            .Setup(h => h.HandleAsync(
                It.Is<UpdateUserCommand>(c => c.UserId == 42),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Success(new UserDto()));

        var result = await _controller.UpdateUser(42, new UserDto { Id = 42 }, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task UpdateUser_NotFound_Returns404()
    {
        _updateUser
            .Setup(h => h.HandleAsync(It.IsAny<UpdateUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.NotFound("User not found"));

        var result = await _controller.UpdateUser(999, new UserDto { Id = 999 }, CancellationToken.None);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task ToggleBan_ReturnsSuccess()
    {
        _toggleBan
            .Setup(h => h.HandleAsync(
                It.Is<ToggleBanCommand>(c => c.UserId == 42),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.ToggleBan(42, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ToggleBan_NotFound_Returns404()
    {
        _toggleBan
            .Setup(h => h.HandleAsync(It.IsAny<ToggleBanCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound("User not found"));

        var result = await _controller.ToggleBan(999, CancellationToken.None);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task ResetPassword_ReturnsSuccess()
    {
        _resetPassword
            .Setup(h => h.HandleAsync(
                It.Is<ResetPasswordCommand>(c => c.UserId == 42 && c.NewPassword == "newSecureP@ss1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.ResetPassword(
            42, new ResetPasswordAdminDto { NewPassword = "newSecureP@ss1" }, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ResetPassword_WeakPassword_Returns400()
    {
        _resetPassword
            .Setup(h => h.HandleAsync(It.IsAny<ResetPasswordCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Слишком короткий пароль"));

        var result = await _controller.ResetPassword(
            42, new ResetPasswordAdminDto { NewPassword = "123" }, CancellationToken.None);

        result.ShouldHaveStatus(400);
    }
}