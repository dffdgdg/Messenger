using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Message;
using Shared.DTO.Message;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class MessagesControllerTests
{
    private readonly Mock<IMessageService> _messageMock;
    private readonly MessagesController _controller;

    public MessagesControllerTests()
    {
        _messageMock = new Mock<IMessageService>();
        _controller = new MessagesController(_messageMock.Object, NullLogger<MessagesController>.Instance);

        AuthHelper.SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task CreateMessage_ServiceReturnsFailure_Returns400()
    {
        var longContent = new string('x', 4001);
        var request = new CreateMessageRequest
        {
            ChatId = 1,
            Content = longContent
        };

        _messageMock.Setup(s => s.CreateMessageAsync(1, request)).ReturnsAsync(Result<MessageDto>.Failure("Сообщение должно содержать текст или файлы"));

        var result = await _controller.CreateMessage(request);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.StatusCode.Should().Be(400);

        var body = bad.Value.Should().BeOfType<ApiResponse<MessageDto>>().Subject;
        body.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateMessage_UserNotMember_Returns403()
    {
        var request = new CreateMessageRequest
        {
            ChatId = 42,
            Content = "Hello!"
        };

        _messageMock.Setup(s => s.CreateMessageAsync(1, request)).ReturnsAsync(Result<MessageDto>.Forbidden("Вы не являетесь участником этого чата"));

        var result = await _controller.CreateMessage(request);

        var forbidden = result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(403);

        var body = forbidden.Value.Should().BeOfType<ApiResponse<MessageDto>>().Subject;
        body.Success.Should().BeFalse();
        body.Error.Should().Contain("участником");
    }
}