using API.Tests.Helpers;
using FluentAssertions;
using Shared.Enum;
using Xunit;

namespace API.Tests.Helpers;

public class ChatBuilderTests : IntegrationTestBase
{
    [Fact]
    public async Task BuildAsync_CreatesChat()
    {
        var user = await DbContextFactory.SeedUserAsync(Context, "creator", "pass");

        var chat = await Context.BuildChat(user.Id)
            .Named("Test Group")
            .BuildAsync();

        chat.Should().NotBeNull();
        chat.Name.Should().Be("Test Group");
        chat.Type.Should().Be(ChatType.Chat);
    }

    [Fact]
    public async Task BuildAsync_AsContact_CreatesContactChat()
    {
        var u1 = await DbContextFactory.SeedUserAsync(Context, "alice", "pass");
        var u2 = await DbContextFactory.SeedUserAsync(Context, "bob", "pass");

        var chat = await Context.BuildChat(u1.Id)
            .AsContact()
            .WithMembers(u2.Id)
            .BuildAsync();

        chat.Type.Should().Be(ChatType.Contact);
        chat.Name.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_DisableHistory_SetsFlag()
    {
        var user = await DbContextFactory.SeedUserAsync(Context, "creator", "pass");

        var chat = await Context.BuildChat(user.Id)
            .Named("Restricted")
            .DisableHistoryForNewMembers()
            .BuildAsync();

        chat.ShowHistoryForNewMembers.Should().BeFalse();
    }
}