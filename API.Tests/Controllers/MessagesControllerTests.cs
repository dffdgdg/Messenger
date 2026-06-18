using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Message;
using Shared.Dto.Search;
using Shared.DTO.Message;
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

    #region Create/Update/Delete

    [Fact]
    public async Task CreateMessage_Success_Returns200()
    {
        var dto = new CreateMessageRequest { ChatId = 10, Content = "Hello" };

        _mock.Setup(s => s.CreateMessageAsync(582, dto))
            .Returns(Result<MessageDto>.Success(new MessageDto()).AsTask());

        var result = await _controller.CreateMessage(dto);

        result.ShouldHaveStatus(200);
        _mock.Verify(s => s.CreateMessageAsync(582, dto), Times.Once);
    }

    [Fact]
    public async Task CreateMessage_Forbidden_Returns403()
    {
        _mock.Setup(s => s.CreateMessageAsync(582, It.IsAny<CreateMessageRequest>()))
            .Returns(Result<MessageDto>.Forbidden("Нет доступа к чату").AsTask());

        var result = await _controller.CreateMessage(new CreateMessageRequest { ChatId = 10 });

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task UpdateMessage_IdMismatch_Returns400WithoutCallingService()
    {
        var result = await _controller.UpdateMessage(8847, new UpdateMessageDto { Id = 2193 });

        result.Should().BeOfType<BadRequestObjectResult>().Which.StatusCode.Should().Be(400);
        _mock.Verify(x => x.UpdateMessageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateMessageDto>()), Times.Never);
    }

    [Fact]
    public async Task UpdateMessage_Success_Returns200()
    {
        var dto = new UpdateMessageDto { Id = 5, Content = "Updated" };

        _mock.Setup(s => s.UpdateMessageAsync(5, 582, dto))
            .Returns(Result<MessageDto>.Success(new MessageDto()).AsTask());

        var result = await _controller.UpdateMessage(5, dto);

        result.ShouldHaveStatus(200);
        _mock.Verify(s => s.UpdateMessageAsync(5, 582, dto), Times.Once);
    }

    [Fact]
    public async Task DeleteMessage_Success_Returns200()
    {
        AuthHelper.SetUser(_controller, userId: 42);

        _mock.Setup(x => x.DeleteMessageAsync(7, 42))
            .Returns(Result.Success().AsTask());

        var result = await _controller.DeleteMessage(7);

        result.ShouldHaveStatus(200);
        _mock.Verify(x => x.DeleteMessageAsync(7, 42), Times.Once);
    }

    [Fact]
    public async Task DeleteMessage_Forbidden_Returns403()
    {
        _mock.Setup(x => x.DeleteMessageAsync(99, 582))
            .Returns(Result.Forbidden("Нет прав").AsTask());

        var result = await _controller.DeleteMessage(99);

        result.ShouldHaveStatus(403);
    }

    #endregion

    #region Pin/Unpin

    [Fact]
    public async Task PinMessage_Success_Returns200()
    {
        _mock.Setup(s => s.PinMessageAsync(7, 582))
            .Returns(Result<MessageDto>.Success(new MessageDto()).AsTask());

        var result = await _controller.PinMessage(7);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task PinMessage_NotFound_Returns404()
    {
        _mock.Setup(x => x.PinMessageAsync(999, 582))
            .Returns(Result<MessageDto>.NotFound("Сообщение не найдено").AsTask());

        var result = await _controller.PinMessage(999);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task UnpinMessage_Success_Returns200()
    {
        _mock.Setup(s => s.UnpinMessageAsync(7, 582))
            .Returns(Result<MessageDto>.Success(new MessageDto()).AsTask());

        var result = await _controller.UnpinMessage(7);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetPinnedMessages_Success_Returns200()
    {
        _mock.Setup(s => s.GetPinnedMessagesAsync(10, 582))
            .Returns(Result<List<MessageDto>>.Success([]).AsTask());

        var result = await _controller.GetPinnedMessages(10);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Get Messages

    [Fact]
    public async Task GetLatestMessages_Success_Returns200()
    {
        _mock.Setup(s => s.GetLatestMessagesAsync(10, 582, 50))
            .Returns(Result<PagedMessagesDto>.Success(new PagedMessagesDto()).AsTask());

        var result = await _controller.GetLatestMessages(10);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetMessagesAround_Success_Returns200()
    {
        _mock.Setup(s => s.GetMessagesAroundAsync(10, 42, 582, 25))
            .Returns(Result<PagedMessagesDto>.Success(new PagedMessagesDto()).AsTask());

        var result = await _controller.GetMessagesAround(10, 42, count: 25);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetMessagesBefore_Success_Returns200()
    {
        _mock.Setup(s => s.GetMessagesBeforeAsync(10, 42, 582, 50))
            .Returns(Result<PagedMessagesDto>.Success(new PagedMessagesDto()).AsTask());

        var result = await _controller.GetMessagesBefore(10, 42, count: 50);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetMessagesAfter_Success_Returns200()
    {
        _mock.Setup(s => s.GetMessagesAfterAsync(10, 42, 582, 15))
            .Returns(Result<PagedMessagesDto>.Success(new PagedMessagesDto()).AsTask());

        var result = await _controller.GetMessagesAfter(10, 42, count: 15);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Search

    [Fact]
    public async Task SearchMessages_Success_Returns200()
    {
        var query = new SearchMessagesQueryDto { Query = "test" };

        _mock.Setup(s => s.SearchMessagesAsync(10, 582, query))
            .Returns(Result<SearchMessagesResponseDto>.Success(new SearchMessagesResponseDto()).AsTask());

        var result = await _controller.SearchMessages(10, query);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GlobalSearch_WrongUserId_Returns403WithoutCallingService()
    {
        var result = await _controller.GlobalSearch(userId: 149, new GlobalSearchQueryDto());

        result.ShouldHaveStatus(403);
        _mock.Verify(x => x.GlobalSearchAsync(It.IsAny<int>(), It.IsAny<GlobalSearchQueryDto>()), Times.Never);
    }

    [Fact]
    public async Task GlobalSearch_OwnUserId_Returns200()
    {
        var query = new GlobalSearchQueryDto { Query = "test" };

        _mock.Setup(s => s.GlobalSearchAsync(582, query))
            .Returns(Result<GlobalSearchResponseDto>.Success(new GlobalSearchResponseDto()).AsTask());

        var result = await _controller.GlobalSearch(userId: 582, query);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Chat Counts

    [Fact]
    public async Task GetChatCounts_Success_Returns200()
    {
        _mock.Setup(s => s.GetChatCountsAsync(10, 582))
            .Returns(Result<ChatCountsDto>.Success(new ChatCountsDto()).AsTask());

        var result = await _controller.GetChatCounts(10);

        result.ShouldHaveStatus(200);
    }

    #endregion
}