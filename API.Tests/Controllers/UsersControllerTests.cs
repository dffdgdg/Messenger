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
    private readonly Mock<IUserService> _userMock = new();
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _controller = new UsersController(_userMock.Object, NullLogger<UsersController>.Instance);
        AuthHelper.SetUser(_controller, userId: 582);
    }

    // ── 403 ownership guard ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_AnotherUserId_Returns403WithoutCallingService()
    {
        var result = await _controller.UpdateUser(id: 731, new UserDto(), CancellationToken.None);

        result.ShouldHaveStatus(403);
        _userMock.Verify(x => x.UpdateUserAsync(
            It.IsAny<int>(), It.IsAny<UserDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_AnotherUserId_Returns403WithoutCallingService()
    {
        var result = await _controller.ChangePassword(id: 731, new ChangePasswordDto(), CancellationToken.None);

        result.ShouldHaveStatus(403);
        _userMock.Verify(x => x.ChangePasswordAsync(
            It.IsAny<int>(), It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadAvatar_AnotherUserId_Returns403WithoutCallingService()
    {
        var result = await _controller.UploadAvatar(id: 731, new Mock<IFormFile>().Object, CancellationToken.None);

        result.ShouldHaveStatus(403);
        _userMock.Verify(x => x.UploadAvatarAsync(
            It.IsAny<int>(), It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeUsername_AnotherUserId_Returns403WithoutCallingService()
    {
        var result = await _controller.ChangeUsername(id: 731, new ChangeUsernameDto(), CancellationToken.None);

        result.ShouldHaveStatus(403);
        _userMock.Verify(x => x.ChangeUsernameAsync(
            It.IsAny<int>(), It.IsAny<ChangeUsernameDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 404 not found ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUser_ServiceReturnsNotFound_Returns404()
    {
        _userMock.Setup(x => x.GetUserAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.NotFound("Пользователь не найден"));

        var result = await _controller.GetUser(999, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<UserDto>>()
            .Which.Success.Should().BeFalse();
    }
}