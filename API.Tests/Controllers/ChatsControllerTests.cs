using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Chat;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class ChatsControllerTests
{
    private readonly Mock<IChatService> _chatMock = new();
    private readonly ChatsController _controller;

    public ChatsControllerTests()
    {
        _controller = new ChatsController(
            _chatMock.Object,
            new Mock<IChatMemberService>().Object,
            NullLogger<ChatsController>.Instance);

        AuthHelper.SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task GetUserDialogs_AnotherUserId_Returns403WithoutCallingService()
    {
        var result = await _controller.GetUserDialogs(userId: 731);

        var body = result.ShouldHaveStatus(403)
            .ShouldHaveBody<ApiResponse<List<ChatDto>>>();

        body.Success.Should().BeFalse();
        _chatMock.Verify(s => s.GetUserDialogsAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateChat_SetsCreatedById()
    {
        AuthHelper.SetUser(_controller, 915);
        var dto = new ChatDto();
        _chatMock.Setup(x => x.CreateChatAsync(It.IsAny<ChatDto>()))
            .ReturnsAsync(Result<ChatDto>.Success(new()));

        await _controller.CreateChat(dto);

        dto.CreatedById.Should().Be(915);
    }
}