using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.ReadReceipt;
using Xunit;

namespace API.Tests.Controllers;

public class ReadReceiptsControllerTests
{
    private readonly Mock<IReadReceiptService> _receiptMock = new();
    private readonly ReadReceiptsController _controller;

    public ReadReceiptsControllerTests()
    {
        _controller = new ReadReceiptsController(_receiptMock.Object, NullLogger<ReadReceiptsController>.Instance);
        AuthHelper.SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task MarkAsRead_Success_Returns200()
    {
        var dto = new MarkAsReadDto { ChatId = 10, MessageId = 42 };

        _receiptMock.Setup(s => s.MarkAsReadAsync(582, dto))
            .Returns(Result<ReadReceiptResponseDto>.Success(new ReadReceiptResponseDto()).AsTask());

        var result = await _controller.MarkAsRead(dto);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task MarkAsRead_NotFound_Returns404()
    {
        var dto = new MarkAsReadDto { ChatId = 10, MessageId = 999 };

        _receiptMock.Setup(s => s.MarkAsReadAsync(582, dto))
            .Returns(Result<ReadReceiptResponseDto>.NotFound("Сообщение не найдено").AsTask());

        var result = await _controller.MarkAsRead(dto);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task GetUnreadCount_Success_Returns200()
    {
        _receiptMock.Setup(s => s.GetUnreadCountAsync(582, 10))
            .Returns(Result<int>.Success(5).AsTask());

        var result = await _controller.GetUnreadCount(10);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUnreadCount_Forbidden_Returns403()
    {
        _receiptMock.Setup(s => s.GetUnreadCountAsync(582, 10))
            .Returns(Result<int>.Forbidden("Нет доступа").AsTask());

        var result = await _controller.GetUnreadCount(10);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task GetAllUnreadCounts_Success_Returns200()
    {
        _receiptMock.Setup(s => s.GetAllUnreadCountsAsync(582))
            .Returns(Result<AllUnreadCountsDto>.Success(new AllUnreadCountsDto()).AsTask());

        var result = await _controller.GetAllUnreadCounts();

        result.ShouldHaveStatus(200);
    }
}