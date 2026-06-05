using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Message;
using Shared.Dto.Search;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class MessagesControllerTests
{
    private readonly Mock<IMessageService> _mock = new();
    private readonly MessagesController _controller;

    public MessagesControllerTests()
    {
        _controller = new MessagesController(_mock.Object, NullLogger<MessagesController>.Instance);
        AuthHelper.SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task DeleteMessage_CallsServiceWithCurrentUserId()
    {
        AuthHelper.SetUser(_controller, userId: 42);
        _mock.Setup(x => x.DeleteMessageAsync(7, 42)).ReturnsAsync(Result.Success());

        var result = await _controller.DeleteMessage(7);

        result.Should().BeOfType<OkObjectResult>();
        _mock.Verify(x => x.DeleteMessageAsync(7, 42), Times.Once);
    }

    [Fact]
    public async Task DeleteMessage_ServiceReturnsForbidden_Returns403()
    {
        _mock.Setup(x => x.DeleteMessageAsync(99, 582))
            .ReturnsAsync(Result.Forbidden("Нет прав для удаления этого сообщения"));

        var result = await _controller.DeleteMessage(99);

        result.ShouldHaveStatus(403)
            .ShouldHaveBody<ApiResponse<object>>()
            .Success.Should().BeFalse();
    }

    [Fact]
    public async Task PinMessage_ServiceReturnsNotFound_Returns404()
    {
        _mock.Setup(x => x.PinMessageAsync(999, 582))
            .ReturnsAsync(Result<MessageDto>.NotFound("Сообщение не найдено"));

        var result = await _controller.PinMessage(999);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<MessageDto>>()
            .Which.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GlobalSearch_AnotherUserId_Returns403WithoutCallingService()
    {
        var result = await _controller.GlobalSearch(userId: 149, new GlobalSearchQueryDto());

        result.ShouldHaveStatus(403);
        _mock.Verify(x => x.GlobalSearchAsync(It.IsAny<int>(), It.IsAny<GlobalSearchQueryDto>()), Times.Never);
    }

    [Fact]
    public async Task UpdateMessage_IdMismatch_Returns400WithoutCallingService()
    {
        var result = await _controller.UpdateMessage(8847, new UpdateMessageDto { Id = 2193 });

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(400);

        _mock.Verify(x => x.UpdateMessageAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateMessageDto>()), Times.Never);
    }
}