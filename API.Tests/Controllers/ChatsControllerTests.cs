using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Chat;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class ChatsControllerTests
{
    private readonly Mock<IChatService> _chatMock;
    private readonly Mock<IChatMemberService> _memberMock;
    private readonly ChatsController _controller;

    public ChatsControllerTests()
    {
        _chatMock = new Mock<IChatService>();
        _memberMock = new Mock<IChatMemberService>();
        _controller = new ChatsController(_chatMock.Object, _memberMock.Object, NullLogger<ChatsController>.Instance);

        AuthHelper.SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task GetUserDialogs_AnotherUserId_Returns403WithoutCallingService()
    {
        var result = await _controller.GetUserDialogs(userId: 99);

        var forbidden = result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(403);

        var body = forbidden.Value.Should().BeOfType<ApiResponse<List<ChatDto>>>().Subject;
        body.Success.Should().BeFalse();

        _chatMock.Verify(s => s.GetUserDialogsAsync(It.IsAny<int>()), Times.Never);
    }
}