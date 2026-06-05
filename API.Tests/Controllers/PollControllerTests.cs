using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Poll;
using Xunit;

namespace API.Tests.Controllers;

public class PollControllerTests
{
    private readonly Mock<IPollService> _pollMock = new();
    private readonly PollsController _controller;

    public PollControllerTests()
    {
        _controller = new PollsController(_pollMock.Object, NullLogger<PollsController>.Instance);
        AuthHelper.SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task Vote_SetsCurrentUserIdInDto()
    {
        AuthHelper.SetUser(_controller, userId: 5);
        var dto = new PollVoteDto();
        _pollMock.Setup(x => x.VoteAsync(dto))
            .ReturnsAsync(Result<PollDto>.Success(new PollDto()));

        await _controller.Vote(dto);

        dto.UserId.Should().Be(5);
    }
}