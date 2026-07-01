using API.Infrastructure.Repositories.Implementations;
using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Enum;

namespace API.Tests.Integration.Repositories;

public class ReadReceiptRepositoryTests : IntegrationTestBase
{
    private readonly ReadReceiptRepository _repo;

    public ReadReceiptRepositoryTests() => _repo = new ReadReceiptRepository(Context);

    [Fact]
    public async Task FindMember_ReturnsMember()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);

        var member = await _repo.FindMemberAsync(chat.Id, u1.Id);

        member.Should().NotBeNull();
        member!.UserId.Should().Be(u1.Id);
    }

    [Fact]
    public async Task FindMember_ReturnsNull_WhenNotMember()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);

        var member = await _repo.FindMemberAsync(chat.Id, u2.Id);
        member.Should().BeNull();
    }

    [Fact]
    public async Task CountUnread_ReturnsCorrectCount()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id, u2.Id]);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Msg1");
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Msg2");
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Msg3");

        var unread = await _repo.CountUnreadAsync(chat.Id, u2.Id, msg1.Id);

        unread.Should().Be(2);
    }

    [Fact]
    public async Task GetUnreadCountsAsync_ReturnsDictionary()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id, u2.Id]);
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Hello");

        var counts = await _repo.GetUnreadCountsAsync(u2.Id, [chat.Id]);

        counts.Should().ContainKey(chat.Id);
        counts[chat.Id].Should().Be(1);
    }

    [Fact]
    public async Task GetAllUnreadCounts_ReturnsOnlyNonZero()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id, u2.Id]);
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Hello");

        var result = await _repo.GetAllUnreadCountsAsync(u2.Id);

        result.Should().HaveCount(1);
        result[0].ChatId.Should().Be(chat.Id);
        result[0].UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task MessageExists_True()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Exists");

        var exists = await _repo.MessageExistsAsync(msg.Id, chat.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task MessageExists_False_WhenDeleted()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Will delete");
        msg.IsDeleted = true;
        await Context.SaveChangesAsync();

        var exists = await _repo.MessageExistsAsync(msg.Id, chat.Id);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetLastMessageId_ReturnsMaxId()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "First");
        var msg2 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Last");

        var lastId = await _repo.GetLastMessageIdAsync(chat.Id);

        lastId.Should().Be(msg2.Id);
    }

    [Fact]
    public async Task GetUnreadInfo_ReturnsFirstUnread()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id, u2.Id]);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Msg1");
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Msg2");

        var info = await _repo.GetUnreadInfoAsync(chat.Id, u2.Id, 0);

        info.Should().NotBeNull();
        info!.Count.Should().Be(2);
        info.FirstUnreadId.Should().Be(msg1.Id);
    }

    [Fact]
    public async Task GetUnreadCountsForUsers_ReturnsPerUserCounts()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var u3 = await TestDataSeeder.SeedUserAsync(Context, "charlie", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id, u2.Id, u3.Id]);
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Hello all");

        var counts = await _repo.GetUnreadCountsForUsersAsync(chat.Id, [u2.Id, u3.Id]);

        counts.Should().ContainKeys([u2.Id, u3.Id]);
        counts[u2.Id].Should().Be(1);
        counts[u3.Id].Should().Be(1);
    }
}