using API.Controllers;
using API.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Online;
using Shared.Dto.User;
using Xunit;

namespace API.Tests.Controllers;

public class UsersControllerTests
{
    private readonly Mock<IUserService> _userMock = new();
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _controller = new UsersController(_userMock.Object, NullLogger<UsersController>.Instance);
        AuthHelper.SetUser(_controller, userId: 582);
    }

    #region Get Users

    [Fact]
    public async Task GetAllUsers_Success_Returns200()
    {
        _userMock.Setup(s => s.GetAllUsersAsync(It.IsAny<CancellationToken>()))
            .Returns(Result<List<UserDto>>.Success([]).AsTask());

        var result = await _controller.GetAllUsers(CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUser_Success_Returns200()
    {
        _userMock.Setup(s => s.GetUserAsync(582, It.IsAny<CancellationToken>()))
            .Returns(Result<UserDto>.Success(new UserDto()).AsTask());

        var result = await _controller.GetUser(582, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUser_NotFound_Returns404()
    {
        _userMock.Setup(x => x.GetUserAsync(999, It.IsAny<CancellationToken>()))
            .Returns(Result<UserDto>.NotFound("Пользователь не найден").AsTask());

        var result = await _controller.GetUser(999, CancellationToken.None);

        result.ShouldHaveStatus(404);
    }

    #endregion

    #region Update User

    [Theory]
    [InlineData(nameof(UsersController.UpdateUser))]
    [InlineData(nameof(UsersController.UploadAvatar))]
    [InlineData(nameof(UsersController.RemoveAvatar))]
    [InlineData(nameof(UsersController.ChangeUsername))]
    [InlineData(nameof(UsersController.ChangePassword))]
    public async Task UserModificationEndpoint_WrongUserId_Returns403WithoutCallingService(string methodName)
    {
        IActionResult result = methodName switch
        {
            nameof(UsersController.UpdateUser) =>
                await _controller.UpdateUser(id: 731, new UserDto(), CancellationToken.None),
            nameof(UsersController.UploadAvatar) =>
                await _controller.UploadAvatar(id: 731, new Mock<IFormFile>().Object, CancellationToken.None),
            nameof(UsersController.RemoveAvatar) =>
                await _controller.RemoveAvatar(id: 731, CancellationToken.None),
            nameof(UsersController.ChangeUsername) =>
                await _controller.ChangeUsername(id: 731, new ChangeUsernameDto(), CancellationToken.None),
            nameof(UsersController.ChangePassword) =>
                await _controller.ChangePassword(id: 731, new ChangePasswordDto(), CancellationToken.None),
            _ => throw new ArgumentException("Unknown method")
        };

        result.ShouldHaveStatus(403);

        // Verify no service calls
        _userMock.Verify(x => x.UpdateUserAsync(It.IsAny<int>(), It.IsAny<UserDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _userMock.Verify(x => x.UploadAvatarAsync(It.IsAny<int>(), It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Never);
        _userMock.Verify(x => x.RemoveAvatarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _userMock.Verify(x => x.ChangeUsernameAsync(It.IsAny<int>(), It.IsAny<ChangeUsernameDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _userMock.Verify(x => x.ChangePasswordAsync(It.IsAny<int>(), It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUser_OwnUserId_Returns200()
    {
        _userMock.Setup(s => s.UpdateUserAsync(582, It.IsAny<UserDto>(), It.IsAny<CancellationToken>()))
            .Returns(Result.Success().AsTask());

        var result = await _controller.UpdateUser(582, new UserDto(), CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Avatar

    [Fact]
    public async Task UploadAvatar_OwnUserId_Returns200()
    {
        var fileMock = new Mock<IFormFile>();

        _userMock.Setup(s => s.UploadAvatarAsync(582, fileMock.Object, It.IsAny<CancellationToken>()))
            .Returns(Result<AvatarResponseDto>.Success(new AvatarResponseDto()).AsTask());

        var result = await _controller.UploadAvatar(582, fileMock.Object, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task RemoveAvatar_OwnUserId_Returns200()
    {
        _userMock.Setup(s => s.RemoveAvatarAsync(582, It.IsAny<CancellationToken>()))
            .Returns(Result.Success().AsTask());

        var result = await _controller.RemoveAvatar(582, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Change Credentials

    [Fact]
    public async Task ChangeUsername_OwnUserId_Returns200()
    {
        _userMock.Setup(s => s.ChangeUsernameAsync(582, It.IsAny<ChangeUsernameDto>(), It.IsAny<CancellationToken>()))
            .Returns(Result.Success().AsTask());

        var result = await _controller.ChangeUsername(582, new ChangeUsernameDto(), CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ChangePassword_OwnUserId_Returns200()
    {
        _userMock.Setup(s => s.ChangePasswordAsync(582, It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
            .Returns(Result.Success().AsTask());

        var result = await _controller.ChangePassword(582, new ChangePasswordDto(), CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Online Status

    [Fact]
    public async Task GetOnlineUsers_Success_Returns200()
    {
        _userMock.Setup(s => s.GetOnlineUsersAsync(It.IsAny<CancellationToken>()))
            .Returns(Result<OnlineUsersResponseDto>.Success(new OnlineUsersResponseDto()).AsTask());

        var result = await _controller.GetOnlineUsers(CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUserOnlineStatus_Success_Returns200()
    {
        var dto = new UserStatusDto(100, true, null, UserStatusType.Online, null);

        _userMock.Setup(s => s.GetOnlineStatusAsync(100, It.IsAny<CancellationToken>()))
            .Returns(Result<UserStatusDto>.Success(dto).AsTask());

        var result = await _controller.GetUserOnlineStatus(100, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUsersOnlineStatus_Success_Returns200()
    {
        var ids = new List<int> { 1, 2, 3 };

        _userMock.Setup(s => s.GetOnlineStatusesAsync(ids, It.IsAny<CancellationToken>()))
            .Returns(Result<List<UserStatusDto>>.Success([]).AsTask());

        var result = await _controller.GetUsersOnlineStatus(ids, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    #endregion
}