using API.Application.Common;
using API.Application.Features.Message;
using API.Application.Features.Message.Commands;
using API.Application.Features.Message.Queries;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.Message;
using Shared.Contracts.Search;
using Xunit;

namespace API.Tests.Controllers;

public class MessagesControllerTests : ControllerTestBase
{
    private readonly Mock<IMessageHandlers> _handlers = new();

    private readonly Mock<ICommandHandler<CreateMessageCommand, MessageDto>> _create = new();
    private readonly Mock<ICommandHandler<UpdateMessageCommand, MessageDto>> _update = new();
    private readonly Mock<ICommandHandler<DeleteMessageCommand>> _delete = new();
    private readonly Mock<ICommandHandler<PinMessageCommand, MessageDto>> _pin = new();
    private readonly Mock<ICommandHandler<UnpinMessageCommand, MessageDto>> _unpin = new();
    private readonly Mock<IQueryHandler<GetLatestMessagesQuery, Result<PagedMessagesDto>>> _getLatest = new();
    private readonly Mock<IQueryHandler<GetMessagesAroundQuery, Result<PagedMessagesDto>>> _getAround = new();
    private readonly Mock<IQueryHandler<GetMessagesBeforeQuery, Result<PagedMessagesDto>>> _getBefore = new();
    private readonly Mock<IQueryHandler<GetMessagesAfterQuery, Result<PagedMessagesDto>>> _getAfter = new();
    private readonly Mock<IQueryHandler<GetPinnedMessagesQuery, Result<List<MessageDto>>>> _getPinned = new();
    private readonly Mock<IQueryHandler<GetChatCountsQuery, Result<ChatCountsDto>>> _getChatCounts = new();
    private readonly Mock<IQueryHandler<SearchMessagesQuery, Result<SearchMessagesResponseDto>>> _search = new();
    private readonly Mock<IQueryHandler<GlobalSearchQuery, Result<GlobalSearchResponseDto>>> _globalSearch = new();

    private readonly MessagesController _controller;

    public MessagesControllerTests()
    {
        _handlers.Setup(h => h.Create).Returns(_create.Object);
        _handlers.Setup(h => h.Update).Returns(_update.Object);
        _handlers.Setup(h => h.Delete).Returns(_delete.Object);
        _handlers.Setup(h => h.Pin).Returns(_pin.Object);
        _handlers.Setup(h => h.Unpin).Returns(_unpin.Object);
        _handlers.Setup(h => h.GetLatest).Returns(_getLatest.Object);
        _handlers.Setup(h => h.GetAround).Returns(_getAround.Object);
        _handlers.Setup(h => h.GetBefore).Returns(_getBefore.Object);
        _handlers.Setup(h => h.GetAfter).Returns(_getAfter.Object);
        _handlers.Setup(h => h.GetPinned).Returns(_getPinned.Object);
        _handlers.Setup(h => h.GetChatCounts).Returns(_getChatCounts.Object);
        _handlers.Setup(h => h.Search).Returns(_search.Object);
        _handlers.Setup(h => h.GlobalSearch).Returns(_globalSearch.Object);

        _controller = new MessagesController(_handlers.Object, NullLogger<MessagesController>.Instance);
        SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task CreateMessage_Success_Returns200()
    {
        _create
            .Setup(h => h.HandleAsync(
                It.Is<CreateMessageCommand>(c => c.SenderId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageDto>.Success(new MessageDto()));

        var result = await _controller.CreateMessage(new CreateMessageRequest { ChatId = 10, Content = "Hello" });

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task CreateMessage_Forbidden_Returns403()
    {
        _create
            .Setup(h => h.HandleAsync(It.IsAny<CreateMessageCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageDto>.Forbidden("Нет доступа к чату"));

        var result = await _controller.CreateMessage(new CreateMessageRequest { ChatId = 10 });

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task UpdateMessage_IdMismatch_Returns400()
    {
        var result = await _controller.UpdateMessage(8847, new UpdateMessageDto { Id = 2193 });

        result.Should().BeOfType<BadRequestObjectResult>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateMessage_Success_Returns200()
    {
        _update
            .Setup(h => h.HandleAsync(
                It.Is<UpdateMessageCommand>(c => c.MessageId == 5 && c.UserId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageDto>.Success(new MessageDto()));

        var result = await _controller.UpdateMessage(5, new UpdateMessageDto { Id = 5, Content = "Updated" });

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task DeleteMessage_Success_Returns200()
    {
        _delete
            .Setup(h => h.HandleAsync(
                It.Is<DeleteMessageCommand>(c => c.MessageId == 7),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.DeleteMessage(7);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task DeleteMessage_Forbidden_Returns403()
    {
        _delete
            .Setup(h => h.HandleAsync(It.IsAny<DeleteMessageCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Forbidden("Нет прав"));

        var result = await _controller.DeleteMessage(99);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task PinMessage_Success_Returns200()
    {
        _pin
            .Setup(h => h.HandleAsync(
                It.Is<PinMessageCommand>(c => c.MessageId == 7),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageDto>.Success(new MessageDto()));

        var result = await _controller.PinMessage(7);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task PinMessage_NotFound_Returns404()
    {
        _pin
            .Setup(h => h.HandleAsync(It.IsAny<PinMessageCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageDto>.NotFound("Сообщение не найдено"));

        var result = await _controller.PinMessage(999);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task UnpinMessage_Success_Returns200()
    {
        _unpin
            .Setup(h => h.HandleAsync(
                It.Is<UnpinMessageCommand>(c => c.MessageId == 7),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageDto>.Success(new MessageDto()));

        var result = await _controller.UnpinMessage(7);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetPinnedMessages_Success_Returns200()
    {
        _getPinned
            .Setup(h => h.HandleAsync(
                It.Is<GetPinnedMessagesQuery>(q => q.ChatId == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<MessageDto>>.Success([]));

        var result = await _controller.GetPinnedMessages(10);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetLatestMessages_Success_Returns200()
    {
        _getLatest
            .Setup(h => h.HandleAsync(It.IsAny<GetLatestMessagesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedMessagesDto>.Success(new PagedMessagesDto()));

        var result = await _controller.GetLatestMessages(10);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetMessagesAround_Success_Returns200()
    {
        _getAround
            .Setup(h => h.HandleAsync(It.IsAny<GetMessagesAroundQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedMessagesDto>.Success(new PagedMessagesDto()));

        var result = await _controller.GetMessagesAround(10, 42);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetChatCounts_Success_Returns200()
    {
        _getChatCounts
            .Setup(h => h.HandleAsync(It.IsAny<GetChatCountsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatCountsDto>.Success(new ChatCountsDto()));

        var result = await _controller.GetChatCounts(10);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GlobalSearch_WrongUserId_Returns403()
    {
        var result = await _controller.GlobalSearch(userId: 149, new GlobalSearchQueryDto());

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task GlobalSearch_OwnUserId_Returns200()
    {
        _globalSearch
            .Setup(h => h.HandleAsync(
                It.Is<GlobalSearchQuery>(q => q.UserId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GlobalSearchResponseDto>.Success(new GlobalSearchResponseDto()));

        var result = await _controller.GlobalSearch(userId: 582, new GlobalSearchQueryDto { Query = "test" });

        result.ShouldHaveStatus(200);
    }
}