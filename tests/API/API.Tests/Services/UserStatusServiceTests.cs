using API.Application.Services.Abstractions;
using API.Application.Services.Features.Status;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Projections;
using API.Domain.Repositories;
using API.Infrastructure.Status;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace API.Tests.Services;

public class UserStatusServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IDepartmentRepository> _deptRepoMock = new();
    private readonly Mock<IOnlineUserService> _onlineMock = new();
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly UserStatusService _service;

    public UserStatusServiceTests()
    {
        _service = new UserStatusService(
            _userRepoMock.Object,
            _deptRepoMock.Object,
            _onlineMock.Object,
            _hubMock.Object,
            TimeProvider.System,
            NullLogger<UserStatusService>.Instance);
    }

    [Fact]
    public async Task GetStatus_UserNotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.GetWithSettingsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserWithSettingsProjection?)null);

        var result = await _service.GetStatusAsync(999);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetStatus_UserFound_ReturnsStatus()
    {
        _userRepoMock.Setup(r => r.GetWithSettingsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserWithSettingsProjection(1, "test", null, null, null, null, null, null, false, null, null, null, true, UserStatusType.Online, null));
        _onlineMock.Setup(o => o.IsOnline(1)).Returns(true);

        var result = await _service.GetStatusAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredStatuses_NoExpired_NoAction()
    {
        _userRepoMock.Setup(r => r.GetExpiredStatusUsersAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _service.Invoking(s => s.CleanupExpiredStatusesAsync()).Should().NotThrowAsync();
    }
}