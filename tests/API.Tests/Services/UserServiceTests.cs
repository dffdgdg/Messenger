using API.Application.Bundles;
using API.Application.Services.Abstractions;
using API.Application.Services.Features.User;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Projections;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.User;
using Xunit;

namespace API.Tests.Services;

public class UserServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IFileService> _fileMock = new();
    private readonly Mock<IOnlineUserService> _onlineMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly UserService _service;

    public UserServiceTests()
    {
        _context = DbContextFactory.Create();
        _unitOfWork = new UnitOfWork(_context, NullLogger<UnitOfWork>.Instance);

        _service = new UserService(
            _unitOfWork,
            new MediaBundle(_fileMock.Object),
            new PresenceBundle(_onlineMock.Object),
            new UrlBundle(_urlMock.Object),
            _userRepoMock.Object,
            NullLogger<UserService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetUser_NotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.GetWithSettingsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserWithSettingsProjection?)null);

        var result = await _service.GetUserAsync(999);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetUser_Found_ReturnsDto()
    {
        var projection = CreateProjection(1, "testuser");
        _userRepoMock.Setup(r => r.GetWithSettingsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);
        _onlineMock.Setup(o => o.IsOnline(1)).Returns(true);
        _urlMock.Setup(u => u.BuildUrl(It.IsAny<string>())).Returns<string?>(s => s);

        var result = await _service.GetUserAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Username.Should().Be("testuser");
        result.Value.IsOnline.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_EmptyCurrent_ReturnsFailure()
    {
        var result = await _service.ChangePasswordAsync(1, new ChangePasswordDto { CurrentPassword = "", NewPassword = "newpass" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("текущий пароль");
    }

    [Fact]
    public async Task ChangePassword_EmptyNew_ReturnsFailure()
    {
        var result = await _service.ChangePasswordAsync(1, new ChangePasswordDto { CurrentPassword = "old", NewPassword = "" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("новый пароль");
    }

    [Fact]
    public async Task ChangePassword_TooShort_ReturnsFailure()
    {
        var result = await _service.ChangePasswordAsync(1, new ChangePasswordDto { CurrentPassword = "old", NewPassword = "12345" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("6 символов");
    }

    [Fact]
    public async Task ChangePassword_UserNotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.FindByIdWithPasswordAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.ChangePasswordAsync(999, new ChangePasswordDto { CurrentPassword = "old", NewPassword = "newpass123" });

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_ReturnsUnauthorized()
    {
        var user = CreateUser("test", "correct");
        _userRepoMock.Setup(r => r.FindByIdWithPasswordAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _service.ChangePasswordAsync(1, new ChangePasswordDto { CurrentPassword = "wrong", NewPassword = "newpass123" });

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);
    }

    [Fact]
    public async Task ChangeUsername_Empty_ReturnsFailure()
    {
        var result = await _service.ChangeUsernameAsync(1, new ChangeUsernameDto { NewUsername = "" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не может быть пустым");
    }

    [Fact]
    public async Task ChangeUsername_InvalidFormat_ReturnsFailure()
    {
        var result = await _service.ChangeUsernameAsync(1, new ChangeUsernameDto { NewUsername = "AB" });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ChangeUsername_Taken_ReturnsConflict()
    {
        _userRepoMock.Setup(r => r.UsernameExistsByOtherUserAsync("taken", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ChangeUsernameAsync(1, new ChangeUsernameDto { NewUsername = "taken" });

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Conflict);
    }

    [Fact]
    public async Task UploadAvatar_NoFile_ReturnsFailure()
    {
        var result = await _service.UploadAvatarAsync(1, null!);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не предоставлен");
    }

    [Fact]
    public async Task UploadAvatar_UserNotFound_ReturnsNotFound()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(100);
        _userRepoMock.Setup(r => r.FindByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.UploadAvatarAsync(999, fileMock.Object);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task RemoveAvatar_UserNotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.FindByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.RemoveAvatarAsync(999);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    private static UserWithSettingsProjection CreateProjection(int id, string username) =>
        new(id, username, null, null, null, null, null, null, false, null, null, null, true, UserStatusType.Online, null);

    private static User CreateUser(string username, string password) =>
        new() { Id = 1, Username = username, Password = UserPassword.Create(password) };
}