using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.User;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class UsersControllerTests
{
    private readonly Mock<IUserService> _userMock;
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _userMock = new Mock<IUserService>();
        _controller = new UsersController(_userMock.Object, NullLogger<UsersController>.Instance);
        AuthHelper.SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task UpdateUser_AnotherUserId_Returns403WithoutCallingService()
    {
        // TC15: Редактирование чужого профиля → 403, сервис не вызывается
        var result = await _controller.UpdateUser(id: 99, new UserDto(), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        _userMock.Verify(x => x.UpdateUserAsync(
            It.IsAny<int>(), It.IsAny<UserDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_AnotherUserId_Returns403WithoutCallingService()
    {
        // TC16: Смена пароля чужого аккаунта → 403, сервис не вызывается
        var result = await _controller.ChangePassword(id: 99, new ChangePasswordDto(), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        _userMock.Verify(x => x.ChangePasswordAsync(
            It.IsAny<int>(), It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadAvatar_AnotherUserId_Returns403WithoutCallingService()
    {
        // TC17: Загрузка аватара от чужого имени → 403, сервис не вызывается
        var file = new Mock<IFormFile>().Object;

        var result = await _controller.UploadAvatar(id: 99, file, CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        _userMock.Verify(x => x.UploadAvatarAsync(
            It.IsAny<int>(), It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeUsername_AnotherUserId_Returns403WithoutCallingService()
    {
        // TC18: Смена логина чужого аккаунта → 403, сервис не вызывается
        var result = await _controller.ChangeUsername(id: 99, new ChangeUsernameDto(), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        _userMock.Verify(x => x.ChangeUsernameAsync(
            It.IsAny<int>(), It.IsAny<ChangeUsernameDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetUser_ServiceReturnsNotFound_Returns404()
    {
        // TC19: Запрос несуществующего пользователя → 404
        _userMock.Setup(x => x.GetUserAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.NotFound("Пользователь не найден"));

        var result = await _controller.GetUser(999, CancellationToken.None);

        var objectResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(404);
        var body = objectResult.Value.Should().BeOfType<ApiResponse<UserDto>>().Subject;
        body.Success.Should().BeFalse();
    }
}