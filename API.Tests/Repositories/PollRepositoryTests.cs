using API.Repositories.Implementations;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Repositories;

public class PollRepositoryTests : IntegrationTestBase
{
    private readonly PollRepository _repo;

    public PollRepositoryTests() => _repo = new PollRepository(Context);

    [Fact]
    public async Task FindByIdWithDetails_ReturnsPollWithOptions()
    {
        var (poll, _) = await DbContextFactory.SeedPollAsync(Context);

        var result = await _repo.FindByIdWithDetailsAsync(poll.Id);

        result.Should().NotBeNull();
        result!.PollOptions.Should().HaveCount(2);
        result.Message.Should().NotBeNull();
    }

    [Fact]
    public async Task FindByIdWithDetails_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.FindByIdWithDetailsAsync(999);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByMessageId_ReturnsPoll()
    {
        var (poll, msg) = await DbContextFactory.SeedPollAsync(Context);

        var result = await _repo.FindByMessageIdAsync(msg.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(poll.Id);
    }

    [Fact]
    public async Task GetUserVotes_ReturnsVotes()
    {
        var (poll, _) = await DbContextFactory.SeedPollAsync(Context);
        var user = await DbContextFactory.SeedUserAsync(Context, "voter", "pass");
        var option = await Context.PollOptions.FirstAsync(o => o.PollId == poll.Id);
        _repo.AddVote(new PollVote { PollId = poll.Id, OptionId = option.Id, UserId = user.Id });
        await Context.SaveChangesAsync();

        var votes = await _repo.GetUserVotesAsync(poll.Id, user.Id);

        votes.Should().HaveCount(1);
        votes[0].OptionId.Should().Be(option.Id);
    }

    [Fact]
    public async Task RemoveVotes_RemovesSpecifiedVotes()
    {
        var (poll, _) = await DbContextFactory.SeedPollAsync(Context);
        var user = await DbContextFactory.SeedUserAsync(Context, "voter", "pass");
        var option = await Context.PollOptions.FirstAsync(o => o.PollId == poll.Id);
        _repo.AddVote(new PollVote { PollId = poll.Id, OptionId = option.Id, UserId = user.Id });
        await Context.SaveChangesAsync();

        var votes = await _repo.GetUserVotesAsync(poll.Id, user.Id);
        _repo.RemoveVotes(votes);
        await Context.SaveChangesAsync();

        var remaining = await _repo.GetUserVotesAsync(poll.Id, user.Id);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task Close_SetsClosesAt()
    {
        var (poll, _) = await DbContextFactory.SeedPollAsync(Context);
        var closeTime = DateTime.UtcNow;

        var p = await Context.Polls.FindAsync(poll.Id);
        p!.ClosesAt = closeTime;
        await Context.SaveChangesAsync();

        var closed = await _repo.FindByIdWithDetailsAsync(poll.Id);
        closed!.ClosesAt.Should().BeCloseTo(closeTime, TimeSpan.FromSeconds(1));
    }
}