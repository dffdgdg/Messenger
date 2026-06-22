using API.Application.Bundles;
using API.Application.Configuration;
using API.Application.Services.Abstractions;
using API.Application.Services.Features.User;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Dto.User;
using Xunit;

namespace API.Tests.Services;

public class AdminServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRefreshTokenRepository> _tokenRepoMock = new();
    private readonly Mock<IDepartmentService> _deptMock = new();
    private readonly Mock<IDepartmentRepository> _deptRepoMock = new();
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly AdminService _service;

    public AdminServiceTests()
    {
        _context = DbContextFactory.Create();
        _unitOfWork = new UnitOfWork(_context, NullLogger<UnitOfWork>.Instance);

        _service = new AdminService(
            _unitOfWork,
            _deptRepoMock.Object,
            new TimeBundle(new AppDateTime(TimeProvider.System)),
            _userRepoMock.Object,
            _tokenRepoMock.Object,
            NullLogger<AdminService>.Instance,
            _hubMock.Object,
            Options.Create(new MessengerSettings { AdminDepartmentId = 1 }),
            _deptMock.Object);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetUsers_ReturnsMappedUsers()
    {
        _userRepoMock.Setup(r => r.GetAllWithSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.GetUsersAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateUser_InvalidUsername_ReturnsFailure()
    {
        var result = await _service.CreateUserAsync(new CreateUserDto
        {
            Username = "AB",
            Password = "password123",
            Surname = "Test",
            Name = "User"
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("3-30 символов");
    }

    [Fact]
    public async Task CreateUser_WeakPassword_ReturnsFailure()
    {
        var result = await _service.CreateUserAsync(new CreateUserDto
        {
            Username = "validuser",
            Password = "12345",
            Surname = "Test",
            Name = "User"
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("6 символов");
    }

    [Fact]
    public async Task CreateUser_EmptySurname_ReturnsFailure()
    {
        var result = await _service.CreateUserAsync(new CreateUserDto
        {
            Username = "validuser",
            Password = "password123",
            Surname = "",
            Name = "User"
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Фамилия");
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_ReturnsConflict()
    {
        _userRepoMock.Setup(r => r.UsernameExistsAsync("taken", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.CreateUserAsync(new CreateUserDto
        {
            Username = "taken",
            Password = "password123",
            Surname = "Test",
            Name = "User"
        });

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Conflict);
    }

    [Fact]
    public async Task UpdateUser_NotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.FindByIdTrackedAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.UpdateUserAsync(999, new UserDto { Id = 999, Username = "valid", Surname = "S", Name = "N" });

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateUser_IdMismatch_ReturnsFailure()
    {
        var result = await _service.UpdateUserAsync(1, new UserDto { Id = 2, Username = "valid", Surname = "S", Name = "N" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Несоответствие");
    }

    [Fact]
    public async Task ToggleBan_NotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.FindByIdTrackedAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.ToggleBanAsync(999);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task ResetPassword_NotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.FindByIdWithPasswordAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.ResetPasswordAsync(999, "newpass123");

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task ResetPassword_WeakPassword_ReturnsFailure()
    {
        var result = await _service.ResetPasswordAsync(1, "12345");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("6 символов");
    }

    [Fact]
    public async Task CreateUser_Success_ReturnsDto()
    {
        _userRepoMock.Setup(r => r.UsernameExistsAsync("newuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _deptMock.Setup(d => d.SyncDepartmentChatMembershipAsync(
            It.IsAny<int>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _userRepoMock.Setup(r => r.GetWithSettingsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Domain.Projections.UserWithSettingsProjection(
                1, "newuser", "Doe", "John", null, null, 1, null, false, null, null, null, true, UserStatusType.Online, null));

        var result = await _service.CreateUserAsync(new CreateUserDto
        {
            Username = "newuser",
            Password = "password123",
            Surname = "Doe",
            Name = "John"
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Username.Should().Be("newuser");
    }
}