using Messenger.Tests.Helpers;

namespace Messenger.Tests.Controllers;

public class ChatsControllerTests
{
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly ChatsController _controller;

    public ChatsControllerTests()
    {
        _chatServiceMock = new Mock<IChatService>();
        _controller = new ChatsController(_chatServiceMock.Object,TestHelpers.CreateLogger<ChatsController>().Object);
        TestHelpers.SetupControllerContext(_controller, userId: 1);
    }

    [Fact]
    public async Task GetUserChats_ReturnsForbidden_WhenAccessingOtherUserChats()
    {
        // Act - пользователь 1 пытается получить чаты пользователя 999
        var result = await _controller.GetUserChats(userId: 999);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetUserChats_ReturnsOk_WhenAccessingOwnChats()
    {
        // Arrange
        _chatServiceMock.Setup(s => s.GetUserChatsAsync(1, It.IsAny<HttpRequest>())).ReturnsAsync([]);

        // Act
        var result = await _controller.GetUserChats(userId: 1);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetChat_ReturnsUnauthorized_WhenNoAccess()
    {
        // Arrange
        _chatServiceMock.Setup(s => s.EnsureUserHasChatAccessAsync(1, 1)).ThrowsAsync(new UnauthorizedAccessException("Нет доступа"));

        // Act
        var result = await _controller.GetChat(chatId: 1);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task CreateChat_ReturnsUnauthorized_WhenCreatingForOtherUser()
    {
        // Arrange - пользователь 1 пытается создать чат от имени пользователя 999
        var dto = new ChatDTO { CreatedById = 999, Name = "Test" };

        // Act
        var result = await _controller.CreateChat(dto);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task DeleteChat_ReturnsUnauthorized_WhenNotOwner()
    {
        // Arrange
        _chatServiceMock.Setup(s => s.DeleteChatAsync(1, 1)).ThrowsAsync(new UnauthorizedAccessException("Только владелец"));

        // Act
        var result = await _controller.DeleteChat(id: 1);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}