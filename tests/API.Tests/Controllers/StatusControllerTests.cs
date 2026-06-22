using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Online;
using Xunit;

namespace API.Tests.Controllers;

public class StatusControllerTests
{
    private readonly Mock<IUserStatusService> _statusMock = new();
    private readonly StatusController _controller;

    public StatusControllerTests()
    {
        _controller = new StatusController(_statusMock.Object, NullLogger<StatusController>.Instance);
        AuthHelper.SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task SetStatus_Success_Returns200()
    {
        var request = new SetStatusRequest { StatusType = UserStatusType.Away, Duration = "1h" };

        _statusMock.Setup(s => s.SetStatusAsync(582, UserStatusType.Away, TimeSpan.FromHours(1)))
            .Returns(Result.Success().AsTask());

        var result = await _controller.SetStatus(request);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task SetStatus_InvalidDuration_Returns400()
    {
        var request = new SetStatusRequest { StatusType = UserStatusType.Away, Duration = "invalid" };

        _statusMock.Setup(s => s.SetStatusAsync(582, UserStatusType.Away, null))
            .Returns(Result.Failure("Неверная длительность").AsTask());

        var result = await _controller.SetStatus(request);

        result.ShouldHaveStatus(400);
    }

    [Fact]
    public async Task GetCurrentStatus_Success_Returns200()
    {
        var dto = new UserStatusDto(582, true, null, UserStatusType.Online, null);

        _statusMock.Setup(s => s.GetStatusAsync(582))
            .Returns(Result<UserStatusDto>.Success(dto).AsTask());

        var result = await _controller.GetCurrentStatus();

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetCurrentStatus_NotFound_Returns404()
    {
        _statusMock.Setup(s => s.GetStatusAsync(582))
            .Returns(Result<UserStatusDto>.NotFound("Пользователь не найден").AsTask());

        var result = await _controller.GetCurrentStatus();

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task GetUserStatus_Success_Returns200()
    {
        var dto = new UserStatusDto(100, false, DateTime.UtcNow, UserStatusType.Away, null);

        _statusMock.Setup(s => s.GetStatusAsync(100))
            .Returns(Result<UserStatusDto>.Success(dto).AsTask());

        var result = await _controller.GetUserStatus(100);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUserStatus_NotFound_Returns404()
    {
        _statusMock.Setup(s => s.GetStatusAsync(999))
            .Returns(Result<UserStatusDto>.NotFound("Пользователь не найден").AsTask());

        var result = await _controller.GetUserStatus(999);

        result.ShouldHaveStatus(404);
    }
}