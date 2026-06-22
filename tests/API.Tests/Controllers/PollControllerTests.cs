using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Message;
using Shared.Dto.Poll;
using Xunit;

namespace API.Tests.Controllers;

public class PollsControllerTests
{
    private readonly Mock<IPollService> _pollMock = new();
    private readonly PollsController _controller;

    public PollsControllerTests()
    {
        _controller = new PollsController(_pollMock.Object, NullLogger<PollsController>.Instance);
        AuthHelper.SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task CreatePoll_Success_Returns200()
    {
        var dto = new CreatePollDto { ChatId = 10, Question = "Q?" };

        _pollMock.Setup(s => s.CreatePollAsync(dto, 1))
            .Returns(Result<MessageDto>.Success(new MessageDto()).AsTask());

        var result = await _controller.CreatePoll(dto);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task CreatePoll_Forbidden_Returns403()
    {
        _pollMock.Setup(s => s.CreatePollAsync(It.IsAny<CreatePollDto>(), 1))
            .Returns(Result<MessageDto>.Forbidden("Нет прав").AsTask());

        var result = await _controller.CreatePoll(new CreatePollDto());

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task Vote_SetsCurrentUserIdAndReturnsSuccess()
    {
        AuthHelper.SetUser(_controller, userId: 5);
        var dto = new PollVoteDto();

        _pollMock.Setup(x => x.VoteAsync(dto))
            .Returns(Result<PollDto>.Success(new PollDto()).AsTask());

        var result = await _controller.Vote(dto);

        dto.UserId.Should().Be(5);
        result.ShouldHaveStatus(200);
    }

    [Theory]
    [InlineData("Уже голосовали", 409)]
    [InlineData("Опрос не найден", 404)]
    public async Task Vote_ServiceError_ReturnsCorrectStatus(string errorMessage, int expectedStatus)
    {
        var dto = new PollVoteDto();
        var result = ResultFactory.CreateByStatusCode<PollDto>(expectedStatus, errorMessage);
        _pollMock.Setup(s => s.VoteAsync(dto)).Returns(result.AsTask());
        var actionResult = await _controller.Vote(dto);
        actionResult.ShouldHaveStatus(expectedStatus);
    }

    [Fact]
    public async Task ClosePoll_Success_Returns200()
    {
        _pollMock.Setup(s => s.ClosePollAsync(42, 1))
            .Returns(Result<PollDto>.Success(new PollDto()).AsTask());

        var result = await _controller.ClosePoll(42);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task ClosePoll_Forbidden_Returns403()
    {
        _pollMock.Setup(s => s.ClosePollAsync(42, 1))
            .Returns(Result<PollDto>.Forbidden("Нет прав").AsTask());

        var result = await _controller.ClosePoll(42);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task GetPoll_Success_Returns200()
    {
        _pollMock.Setup(s => s.GetPollAsync(42, 1))
            .Returns(Result<PollDto>.Success(new PollDto()).AsTask());

        var result = await _controller.GetPoll(42);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetPoll_NotFound_Returns404()
    {
        _pollMock.Setup(s => s.GetPollAsync(999, 1))
            .Returns(Result<PollDto>.NotFound("Опрос не найден").AsTask());

        var result = await _controller.GetPoll(999);

        result.ShouldHaveStatus(404);
    }
}