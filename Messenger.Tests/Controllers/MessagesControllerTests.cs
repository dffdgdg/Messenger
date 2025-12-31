using Messenger.Tests.Helpers;

namespace Messenger.Tests.Controllers;

public class MessagesControllerTests
{
    private readonly Mock<IMessageService> _messageServiceMock;
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly MessagesController _controller;

    public MessagesControllerTests()
    {
        _messageServiceMock = new Mock<IMessageService>();
        _chatServiceMock = new Mock<IChatService>();
        _controller = new MessagesController(_messageServiceMock.Object,_chatServiceMock.Object,TestHelpers.CreateLogger<MessagesController>().Object);
        TestHelpers.SetupControllerContext(_controller, userId: 1);
    }

    [Fact]
    public async Task GetChatMessages_ReturnsUnauthorized_WhenNoAccess()
    {
        // Arrange
        _chatServiceMock
            .Setup(s => s.EnsureUserHasChatAccessAsync(1, 1))
            .ThrowsAsync(new UnauthorizedAccessException("Нет доступа"));

        // Act
        var result = await _controller.GetChatMessages(chatId: 1);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task UpdateMessage_ReturnsBadRequest_WhenIdMismatch()
    {
        // Arrange
        var dto = new UpdateMessageDTO { Id = 999, Content = "Updated" };

        // Act
        var result = await _controller.UpdateMessage(id: 1, dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateMessage_ReturnsUnauthorized_WhenNotOwner()
    {
        // Arrange
        var dto = new UpdateMessageDTO { Id = 1, Content = "Updated" };
        _messageServiceMock.Setup(s => s.UpdateMessageAsync(1, 1, dto, It.IsAny<HttpRequest>())).ThrowsAsync(new UnauthorizedAccessException("Не ваше сообщение"));

        // Act
        var result = await _controller.UpdateMessage(id: 1, dto);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GlobalSearch_ReturnsForbidden_WhenSearchingForOtherUser()
    {
        // Act - пользователь 1 пытается искать от имени пользователя 999
        var result = await _controller.GlobalSearch(userId: 999, query: "test");

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }
}