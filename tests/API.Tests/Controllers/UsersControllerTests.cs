using API.Application.Common;
using API.Application.Features.User;
using API.Application.Features.User.Commands;
using API.Application.Features.User.Queries;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.Online;
using Shared.Contracts.User;
using Shared.Enum;
using Xunit;

namespace API.Tests.Controllers;

public class UsersControllerTests : ControllerTestBase
{
    private readonly Mock<IUserHandlers> _handlers = new();

    private readonly Mock<ICommandHandler<ChangePasswordCommand>> _changePassword = new();
    private readonly Mock<ICommandHandler<ChangeUsernameCommand>> _changeUsername = new();
    private readonly Mock<ICommandHandler<RemoveUserAvatarCommand>> _removeAvatar = new();
    private readonly Mock<ICommandHandler<UpdateUserCommand>> _updateUser = new();
    private readonly Mock<ICommandHandler<UploadUserAvatarCommand, AvatarResponseDto>> _uploadAvatar = new();
    private readonly Mock<IQueryHandler<GetAllUsersQuery, Result<List<UserDto>>>> _getUsers = new();
    private readonly Mock<IQueryHandler<GetUserQuery, Result<UserDto>>> _getUser = new();
    private readonly Mock<IQueryHandler<GetOnlineUsersQuery, Result<OnlineUsersResponseDto>>> _getOnlineUsers = new();
    private readonly Mock<IQueryHandler<GetUserStatusQuery, Result<UserStatusDto>>> _getUserStatus = new();
    private readonly Mock<IQueryHandler<GetUserStatusesQuery, Result<List<UserStatusDto>>>> _getUserStatuses = new();

    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _handlers.Setup(h => h.ChangePassword).Returns(_changePassword.Object);
        _handlers.Setup(h => h.ChangeUsername).Returns(_changeUsername.Object);
        _handlers.Setup(h => h.RemoveAvatar).Returns(_removeAvatar.Object);
        _handlers.Setup(h => h.UpdateUser).Returns(_updateUser.Object);
        _handlers.Setup(h => h.UploadAvatar).Returns(_uploadAvatar.Object);
        _handlers.Setup(h => h.GetUsers).Returns(_getUsers.Object);
        _handlers.Setup(h => h.GetUser).Returns(_getUser.Object);
        _handlers.Setup(h => h.GetOnlineUsers).Returns(_getOnlineUsers.Object);
        _handlers.Setup(h => h.GetUserStatus).Returns(_getUserStatus.Object);
        _handlers.Setup(h => h.GetUserStatuses).Returns(_getUserStatuses.Object);

        _controller = new UsersController(_handlers.Object, NullLogger<UsersController>.Instance);
        SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task GetAllUsers_Success_Returns200()
    {
        _getUsers
            .Setup(h => h.HandleAsync(It.IsAny<GetAllUsersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<UserDto>>.Success([]));

        var result = await _controller.GetAllUsers(CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUser_Success_Returns200()
    {
        _getUser
            .Setup(h => h.HandleAsync(
                It.Is<GetUserQuery>(q => q.UserId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Success(new UserDto()));

        var result = await _controller.GetUser(582, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUser_NotFound_Returns404()
    {
        _getUser
            .Setup(h => h.HandleAsync(It.IsAny<GetUserQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.NotFound("Пользователь не найден"));

        var result = await _controller.GetUser(999, CancellationToken.None);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task UpdateUser_WrongUser_Returns403()
    {
        var result = await _controller.UpdateUser(731, new UserDto(), CancellationToken.None);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task UpdateUser_OwnUser_Returns200()
    {
        _updateUser
            .Setup(h => h.HandleAsync(It.IsAny<UpdateUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.UpdateUser(582, new UserDto { Id = 582 }, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task UploadAvatar_OwnUser_Returns200()
    {
        _uploadAvatar
            .Setup(h => h.HandleAsync(It.IsAny<UploadUserAvatarCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AvatarResponseDto>.Success(new AvatarResponseDto()));

        var result = await _controller.UploadAvatar(582, new Mock<IFormFile>().Object, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task RemoveAvatar_OwnUser_Returns200()
    {
        _removeAvatar
            .Setup(h => h.HandleAsync(It.IsAny<RemoveUserAvatarCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.RemoveAvatar(582, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ChangeUsername_OwnUser_Returns200()
    {
        _changeUsername
            .Setup(h => h.HandleAsync(It.IsAny<ChangeUsernameCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.ChangeUsername(582, new ChangeUsernameDto(), CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ChangePassword_OwnUser_Returns200()
    {
        _changePassword
            .Setup(h => h.HandleAsync(It.IsAny<ChangePasswordCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.ChangePassword(582, new ChangePasswordDto(), CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetOnlineUsers_Success_Returns200()
    {
        _getOnlineUsers
            .Setup(h => h.HandleAsync(It.IsAny<GetOnlineUsersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnlineUsersResponseDto>.Success(new OnlineUsersResponseDto()));

        var result = await _controller.GetOnlineUsers(CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUserOnlineStatus_Success_Returns200()
    {
        _getUserStatus
            .Setup(h => h.HandleAsync(
                It.Is<GetUserStatusQuery>(q => q.UserId == 100),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserStatusDto>.Success(
                new UserStatusDto(100, true, null, UserStatusType.Online, null)));

        var result = await _controller.GetUserOnlineStatus(100, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }
}