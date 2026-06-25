using API.Application.Common;
using API.Application.Features.ReadReceipt;
using API.Application.Features.ReadReceipt.Commands;
using API.Application.Features.ReadReceipt.Queries;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.ReadReceipt;
using Xunit;

namespace API.Tests.Controllers;

public class ReadReceiptsControllerTests : ControllerTestBase
{
    private readonly Mock<IReadReceiptHandlers> _handlers = new();

    private readonly Mock<ICommandHandler<MarkAsReadCommand, ReadReceiptResponseDto>> _markAsRead = new();
    private readonly Mock<IQueryHandler<GetAllUnreadCountsQuery, Result<AllUnreadCountsDto>>> _getAllUnread = new();
    private readonly Mock<IQueryHandler<GetUnreadCountQuery, Result<int>>> _getUnread = new();

    private readonly ReadReceiptsController _controller;

    public ReadReceiptsControllerTests()
    {
        _handlers.Setup(h => h.MarkAsRead).Returns(_markAsRead.Object);
        _handlers.Setup(h => h.GetAllUnreadCounts).Returns(_getAllUnread.Object);
        _handlers.Setup(h => h.GetUnreadCounts).Returns(_getUnread.Object);

        _controller = new ReadReceiptsController(_handlers.Object, NullLogger<ReadReceiptsController>.Instance);
        SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task MarkAsRead_Success_Returns200()
    {
        _markAsRead
            .Setup(h => h.HandleAsync(
                It.Is<MarkAsReadCommand>(c => c.UserId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReadReceiptResponseDto>.Success(new ReadReceiptResponseDto()));

        var result = await _controller.MarkAsRead(new MarkAsReadDto { ChatId = 10, MessageId = 42 });

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task MarkAsRead_Failure_Returns400()
    {
        _markAsRead
            .Setup(h => h.HandleAsync(It.IsAny<MarkAsReadCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReadReceiptResponseDto>.Failure("Сообщение не найдено"));

        var result = await _controller.MarkAsRead(new MarkAsReadDto { ChatId = 10, MessageId = 999 });

        result.ShouldHaveStatus(400);
    }

    [Fact]
    public async Task GetUnreadCount_Success_Returns200()
    {
        _getUnread
            .Setup(h => h.HandleAsync(
                It.Is<GetUnreadCountQuery>(q => q.UserId == 582 && q.ChatId == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(5));

        var result = await _controller.GetUnreadCount(10);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetAllUnreadCounts_Success_Returns200()
    {
        _getAllUnread
            .Setup(h => h.HandleAsync(
                It.Is<GetAllUnreadCountsQuery>(q => q.UserId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AllUnreadCountsDto>.Success(new AllUnreadCountsDto()));

        var result = await _controller.GetAllUnreadCounts();

        result.ShouldHaveStatus(200);
    }
}