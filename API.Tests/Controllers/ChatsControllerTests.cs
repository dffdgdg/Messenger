using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Chat;
using Shared.Dto.Poll;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class ChatsControllerTests
{
    private readonly Mock<IChatService> _chatMock;
    private readonly Mock<IPollService> _pollMock;
    private readonly Mock<IChatMemberService> _memberMock;
    private readonly ChatsController _controller;

    public ChatsControllerTests()
    {
        _chatMock = new Mock<IChatService>();
        _memberMock = new Mock<IChatMemberService>();
        _pollMock = new Mock<IPollService>();
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
    [Fact]
    public async Task GetChat_ReturnsChat()
    {
        var dto = new ChatDto { Id = 1 };

        _chatMock.Setup(x => x.GetChatForUserAsync(1, 1)).ReturnsAsync(Result<ChatDto>.Success(dto));

        var result = await _controller.GetChat(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;

        var body = ok.Value.Should().BeOfType<ApiResponse<ChatDto>>().Subject;

        body.Data!.Id.Should().Be(1);
    }
    [Fact]
    public async Task CreateChat_SetsCreatedById()
    {
        AuthHelper.SetUser(_controller, 77);

        var dto = new ChatDto();

        _chatMock.Setup(x => x.CreateChatAsync(It.IsAny<ChatDto>())).ReturnsAsync(Result<ChatDto>.Success(new()));

        await _controller.CreateChat(dto);

        dto.CreatedById.Should().Be(77);
    }
}