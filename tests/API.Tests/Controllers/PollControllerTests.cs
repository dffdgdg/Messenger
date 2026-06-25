using API.Application.Common;
using API.Application.Features.Poll;
using API.Application.Features.Poll.Commands;
using API.Application.Features.Poll.Queries;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.Message;
using Shared.Contracts.Poll;
using Xunit;

namespace API.Tests.Controllers;

public class PollsControllerTests : ControllerTestBase
{
    private readonly Mock<IPollHandlers> _handlers = new();

    private readonly Mock<ICommandHandler<ClosePollCommand, PollDto>> _closePoll = new();
    private readonly Mock<ICommandHandler<CreatePollCommand, MessageDto>> _createPoll = new();
    private readonly Mock<ICommandHandler<VotePollCommand, PollDto>> _votePoll = new();
    private readonly Mock<IQueryHandler<GetPollQuery, Result<PollDto>>> _getPoll = new();

    private readonly PollsController _controller;

    public PollsControllerTests()
    {
        _handlers.Setup(h => h.ClosePoll).Returns(_closePoll.Object);
        _handlers.Setup(h => h.CreatePoll).Returns(_createPoll.Object);
        _handlers.Setup(h => h.VotePoll).Returns(_votePoll.Object);
        _handlers.Setup(h => h.GetPoll).Returns(_getPoll.Object);

        _controller = new PollsController(_handlers.Object, NullLogger<PollsController>.Instance);
        SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task CreatePoll_Success_Returns200()
    {
        _createPoll
            .Setup(h => h.HandleAsync(It.IsAny<CreatePollCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageDto>.Success(new MessageDto()));

        var result = await _controller.CreatePoll(new CreatePollDto
        {
            ChatId = 10,
            Question = "Q?",
            Options = [new CreatePollOptionDto { Text = "A" }, new CreatePollOptionDto { Text = "B" }]
        });

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task CreatePoll_Forbidden_Returns403()
    {
        _createPoll
            .Setup(h => h.HandleAsync(It.IsAny<CreatePollCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageDto>.Forbidden("Нет прав"));

        var result = await _controller.CreatePoll(new CreatePollDto
        {
            Options = [new CreatePollOptionDto { Text = "A" }, new CreatePollOptionDto { Text = "B" }]
        });

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task Vote_SetsUserId_Returns200()
    {
        SetUser(_controller, userId: 5);

        _votePoll
            .Setup(h => h.HandleAsync(
                It.Is<VotePollCommand>(c => c.Dto.UserId == 5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PollDto>.Success(new PollDto()));

        var result = await _controller.Vote(new PollVoteDto());

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ClosePoll_Success_Returns200()
    {
        _closePoll
            .Setup(h => h.HandleAsync(
                It.Is<ClosePollCommand>(c => c.PollId == 42),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PollDto>.Success(new PollDto()));

        var result = await _controller.ClosePoll(42);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ClosePoll_Forbidden_Returns403()
    {
        _closePoll
            .Setup(h => h.HandleAsync(It.IsAny<ClosePollCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PollDto>.Forbidden("Нет прав"));

        var result = await _controller.ClosePoll(42);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task GetPoll_Success_Returns200()
    {
        _getPoll
            .Setup(h => h.HandleAsync(
                It.Is<GetPollQuery>(q => q.PollId == 42),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PollDto>.Success(new PollDto()));

        var result = await _controller.GetPoll(42);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetPoll_NotFound_Returns404()
    {
        _getPoll
            .Setup(h => h.HandleAsync(It.IsAny<GetPollQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PollDto>.NotFound("Опрос не найден"));

        var result = await _controller.GetPoll(999);

        result.ShouldHaveStatus(404);
    }
}