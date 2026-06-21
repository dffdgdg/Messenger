using API.Common.Patterns;
using API.Data;
using API.Repositories.Abstarctions;
using API.Services.Abstractions;
using API.Services.Infrastructure.Bundles;
using API.Services.Messaging;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Poll;
using Shared.Enum;
using Xunit;

namespace API.Tests.Services;

public class PollServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IPollRepository> _pollRepoMock = new();
    private readonly Mock<IMessageRepository> _msgRepoMock = new();
    private readonly Mock<IAccessControlService> _accessMock = new();
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly PollService _service;

    public PollServiceTests()
    {
        _context = DbContextFactory.Create();

        var timeBundle = new TimeBundle(new AppDateTime(TimeProvider.System));

        _service = new PollService(
            _context,
            _pollRepoMock.Object,
            _msgRepoMock.Object,
            _accessMock.Object,
            _hubMock.Object,
            _urlMock.Object,
            timeBundle,
            NullLogger<PollService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetPoll_NotFound_ReturnsNotFound()
    {
        _pollRepoMock.Setup(r => r.FindByIdWithDetailsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Poll?)null);

        var result = await _service.GetPollAsync(999, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetPoll_NoAccess_ReturnsForbidden()
    {
        var poll = CreatePollWithMessage();
        _pollRepoMock.Setup(r => r.FindByIdWithDetailsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(poll);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.GetPollAsync(1, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task CreatePoll_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.CreatePollAsync(new CreatePollDto { ChatId = 10 }, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task CreatePoll_EmptyQuestion_ReturnsFailure()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);

        var result = await _service.CreatePollAsync(
            new CreatePollDto { ChatId = 10, Question = "", Options = [new() { Text = "A" }, new() { Text = "B" }] }, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Вопрос");
    }

    [Fact]
    public async Task CreatePoll_TooFewOptions_ReturnsFailure()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);

        var result = await _service.CreatePollAsync(
            new CreatePollDto { ChatId = 10, Question = "Q?", Options = [new() { Text = "A" }] }, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("2 варианта");
    }

    [Fact]
    public async Task Vote_PollNotFound_ReturnsNotFound()
    {
        _pollRepoMock.Setup(r => r.FindByIdWithDetailsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Poll?)null);

        var result = await _service.VoteAsync(new PollVoteDto { PollId = 999, UserId = 1 });

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task Vote_NoAccess_ReturnsForbidden()
    {
        var poll = CreatePollWithMessage();
        _pollRepoMock.Setup(r => r.FindByIdWithDetailsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(poll);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.VoteAsync(new PollVoteDto { PollId = 1, UserId = 1 });

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task Vote_InvalidOption_ReturnsFailure()
    {
        var poll = CreatePollWithMessage();
        _pollRepoMock.Setup(r => r.FindByIdWithDetailsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(poll);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        _pollRepoMock.Setup(r => r.GetUserVotesAsync(1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.VoteAsync(new PollVoteDto { PollId = 1, UserId = 1, OptionId = 999 });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Невалидные варианты");
    }

    [Fact]
    public async Task ClosePoll_NotFound_ReturnsNotFound()
    {
        _pollRepoMock.Setup(r => r.FindByIdWithDetailsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Poll?)null);

        var result = await _service.ClosePollAsync(999, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task ClosePoll_NotAuthor_ReturnsFailure()
    {
        var poll = CreatePollWithMessage(senderId: 999);
        _pollRepoMock.Setup(r => r.FindByIdWithDetailsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(poll);
        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(false);
        _accessMock.Setup(a => a.IsOwnerAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.ClosePollAsync(1, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Недостаточно прав");
    }

    private static Poll CreatePollWithMessage(int senderId = 1)
    {
        var message = new UserMessage
        {
            Id = 1,
            ChatId = 10,
            SenderId = senderId,
            Content = "Poll?",
            IsDeleted = false
        };
        var poll = new Poll
        {
            Id = 1,
            MessageId = 1,
            Message = message,
            IsAnonymous = false,
            AllowsMultipleAnswers = false
        };
        poll.PollOptions.Add(new PollOption { Id = 1, PollId = 1, OptionText = "A", Position = 0 });
        poll.PollOptions.Add(new PollOption { Id = 2, PollId = 1, OptionText = "B", Position = 1 });
        return poll;
    }
}