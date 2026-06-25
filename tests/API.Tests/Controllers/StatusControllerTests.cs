using API.Application.Common;
using API.Application.Features.Status;
using API.Application.Features.Status.Commands;
using API.Application.Features.Status.Queries;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.Online;
using Shared.Enum;
using Xunit;

namespace API.Tests.Controllers;

public class StatusControllerTests : ControllerTestBase
{
    private readonly Mock<IStatusHandlers> _handlers = new();

    private readonly Mock<ICommandHandler<SetStatusCommand>> _setStatus = new();
    private readonly Mock<IQueryHandler<GetStatusQuery, Result<UserStatusDto>>> _getStatus = new();

    private readonly StatusController _controller;

    public StatusControllerTests()
    {
        _handlers.Setup(h => h.SetStatus).Returns(_setStatus.Object);
        _handlers.Setup(h => h.GetStatus).Returns(_getStatus.Object);

        _controller = new StatusController(_handlers.Object, NullLogger<StatusController>.Instance);
        SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task SetStatus_Success_Returns200()
    {
        _setStatus
            .Setup(h => h.HandleAsync(
                It.Is<SetStatusCommand>(c => c.UserId == 582 && c.StatusType == UserStatusType.Away),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.SetStatus(
            new SetStatusRequest { StatusType = UserStatusType.Away, Duration = "1h" });

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task SetStatus_InvalidDuration_Returns400()
    {
        _setStatus
            .Setup(h => h.HandleAsync(It.IsAny<SetStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Неверная длительность"));

        var result = await _controller.SetStatus(
            new SetStatusRequest { StatusType = UserStatusType.Away, Duration = "invalid" });

        result.ShouldHaveStatus(400);
    }

    [Fact]
    public async Task GetCurrentStatus_Success_Returns200()
    {
        _getStatus
            .Setup(h => h.HandleAsync(
                It.Is<GetStatusQuery>(q => q.UserId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserStatusDto>.Success(
                new UserStatusDto(582, true, null, UserStatusType.Online, null)));

        var result = await _controller.GetCurrentStatus();

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetCurrentStatus_NotFound_Returns404()
    {
        _getStatus
            .Setup(h => h.HandleAsync(It.IsAny<GetStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserStatusDto>.NotFound("Пользователь не найден"));

        var result = await _controller.GetCurrentStatus();

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task GetUserStatus_Success_Returns200()
    {
        _getStatus
            .Setup(h => h.HandleAsync(
                It.Is<GetStatusQuery>(q => q.UserId == 100),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserStatusDto>.Success(
                new UserStatusDto(100, false, DateTime.UtcNow, UserStatusType.Away, null)));

        var result = await _controller.GetUserStatus(100);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUserStatus_NotFound_Returns404()
    {
        _getStatus
            .Setup(h => h.HandleAsync(It.IsAny<GetStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserStatusDto>.NotFound("Пользователь не найден"));

        var result = await _controller.GetUserStatus(999);

        result.ShouldHaveStatus(404);
    }
}