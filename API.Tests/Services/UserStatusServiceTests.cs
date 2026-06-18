using API.Data;
using API.Hubs;
using API.Services.Infrastructure;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Online;
using Shared.Enum;
using Xunit;

namespace API.Tests.Services;

public class UserStatusServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IOnlineUserService> _onlineMock = new();
    private readonly Mock<IHubContext<MessengerHub>> _hubMock = new();
    private readonly UserStatusService _service;

    public UserStatusServiceTests()
    {
        _context = DbContextFactory.Create();
        _hubMock.Setup(h => h.Clients).Returns(Mock.Of<IHubClients>());

        _service = new UserStatusService(
            _context,
            _onlineMock.Object,
            _hubMock.Object,
            TimeProvider.System,
            NullLogger<UserStatusService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetStatus_UserNotFound_ReturnsNotFound()
    {
        var result = await _service.GetStatusAsync(999);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetStatus_UserFound_ReturnsStatus()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "testuser", "pass");
        _onlineMock.Setup(o => o.IsOnline(user.Id)).Returns(true);

        var result = await _service.GetStatusAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task CleanupExpiredStatuses_NoExpired_NoAction() => 
        await _service.Invoking(s => s.CleanupExpiredStatusesAsync()).Should().NotThrowAsync();
}