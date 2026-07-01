using API.Domain.Entities;
using API.Infrastructure.Repositories.Implementations;
using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.Enum;

namespace API.Tests.Integration.Repositories;

public class MessageRepositoryTests : IntegrationTestBase
{
    private readonly MessageRepository _repo;

    public MessageRepositoryTests() => _repo = new MessageRepository(Context);

    [Fact]
    public async Task FindUserMessageById_ReturnsMessage()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Hello");

        var result = await _repo.FindUserMessageByIdAsync(msg.Id);

        result.Should().NotBeNull();
        result!.Content.Should().Be("Hello");
    }

    [Fact]
    public async Task FindUserMessageById_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.FindUserMessageByIdAsync(999);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindUserMessageWithIncludes_ReturnsWithRelations()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Test");

        var result = await _repo.FindUserMessageWithIncludesAsync(msg.Id);

        result.Should().NotBeNull();
        result!.Sender.Should().NotBeNull();
        result.Sender!.Username.Should().Be("sender");
    }

    [Fact]
    public async Task GetBefore_ReturnsOlderMessages()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "First");
        var msg2 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Second");
        var msg3 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Third");

        var result = await _repo.GetBeforeAsync(chat.Id, msg3.Id, 2);

        result.Should().HaveCount(2);
        result.Select(m => m.Id).Should().Contain([msg1.Id, msg2.Id]);
    }

    [Fact]
    public async Task GetAfter_ReturnsNewerMessages()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "First");
        var msg2 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Second");
        var msg3 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Third");

        var result = await _repo.GetAfterAsync(chat.Id, msg1.Id, 2);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(msg2.Id);
        result[1].Id.Should().Be(msg3.Id);
    }

    [Fact]
    public async Task GetLatest_ReturnsMostRecent()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Msg1");
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Msg2");
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Msg3");

        var (messages, hasOlder) = await _repo.GetLatestAsync(chat.Id, 2);

        messages.Should().HaveCount(2);
        hasOlder.Should().BeTrue();
    }

    [Fact]
    public async Task GetPinned_ReturnsOnlyPinned()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Not pinned");
        var msg2 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Pinned one");

        var m = await Context.Messages.FindAsync(msg2.Id);
        m!.PinnedAt = DateTime.UtcNow;
        m.PinnedByUserId = user.Id;
        await Context.SaveChangesAsync();

        var pinned = await _repo.GetPinnedAsync(chat.Id);

        pinned.Should().HaveCount(1);
        pinned[0].Id.Should().Be(msg2.Id);
    }

    [Fact]
    public async Task HasOlder_True_WhenOlderExists()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Older");
        var msg2 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Newer");

        var result = await _repo.HasOlderAsync(chat.Id, msg2.Id, null);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasOlder_False_WhenNoOlder()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Only message");

        var result = await _repo.HasOlderAsync(chat.Id, msg1.Id, null);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasNewer_True_WhenNewerExists()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg1 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "First");
        var msg2 = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Second");

        var result = await _repo.HasNewerAsync(chat.Id, msg1.Id, null);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsInChat_True()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Exists");

        var result = await _repo.ExistsInChatAsync(msg.Id, chat.Id);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsInChat_False_WhenDifferentChat()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat1 = await TestDataSeeder.SeedChatAsync(Context, user.Id, ChatType.Chat, "Chat1", [user.Id]);
        var chat2 = await TestDataSeeder.SeedChatAsync(Context, user.Id, ChatType.Chat, "Chat2", [user.Id]);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat1.Id, user.Id, "In chat1");

        var result = await _repo.ExistsInChatAsync(msg.Id, chat2.Id);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDelete_MarksDeleted()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Will be deleted");

        msg.IsDeleted = true;
        msg.Content = null;
        msg.ReplyToMessageId = null;
        msg.ForwardedFromMessageId = null;
        msg.EditedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        var deleted = await Context.UserMessages.FirstOrDefaultAsync(m => m.Id == msg.Id);
        deleted!.IsDeleted.Should().BeTrue();
        deleted.Content.Should().BeNull();
    }

    [Fact]
    public async Task PinAsync_SetsPinnedAt()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Pin me");
        var pinTime = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        msg.PinnedAt = pinTime;
        msg.PinnedByUserId = user.Id;
        await Context.SaveChangesAsync();

        var pinned = await _repo.FindUserMessageByIdAsync(msg.Id);
        pinned!.PinnedAt.Should().Be(pinTime);
        pinned.PinnedByUserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task UnpinAsync_ClearsPinnedAt()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Unpin me");
        msg.PinnedAt = DateTime.UtcNow;
        msg.PinnedByUserId = user.Id;
        await Context.SaveChangesAsync();

        msg.PinnedAt = null;
        msg.PinnedByUserId = null;
        await Context.SaveChangesAsync();

        var unpinned = await _repo.FindUserMessageByIdAsync(msg.Id);
        unpinned!.PinnedAt.Should().BeNull();
        unpinned.PinnedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task GetForwardedToChatIds_ReturnsDistinctChatIds()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat1 = await TestDataSeeder.SeedChatAsync(Context, user.Id, ChatType.Chat, "Chat1", [user.Id]);
        var chat2 = await TestDataSeeder.SeedChatAsync(Context, user.Id, ChatType.Chat, "Chat2", [user.Id]);
        var original = await TestDataSeeder.SeedMessageAsync(Context, chat1.Id, user.Id, "Original");
        await TestDataSeeder.SeedMessageAsync(Context, chat2.Id, user.Id, "Forwarded", forwardFromId: original.Id);
        await TestDataSeeder.SeedMessageAsync(Context, chat2.Id, user.Id, "Also forwarded", forwardFromId: original.Id);

        var chatIds = await _repo.GetForwardedToChatIdsAsync(original.Id);

        chatIds.Should().Contain([chat1.Id, chat2.Id]);
    }

    [Fact]
    public async Task GetChatCounts_ReturnsCorrectCounts()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "With file");
        Context.MessageFiles.Add(new MessageFile
        {
            MessageId = msg.Id,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            Path = "/files/doc.pdf"
        });
        Context.MessageFiles.Add(new MessageFile
        {
            MessageId = msg.Id,
            FileName = "pic.png",
            ContentType = "image/png",
            Path = "/files/pic.png"
        });
        await Context.SaveChangesAsync();

        var counts = await _repo.GetChatCountsAsync(chat.Id, null);

        counts.MediaCount.Should().Be(1);
        counts.FilesCount.Should().Be(1);
        counts.PollsCount.Should().Be(0);
        counts.PinnedCount.Should().Be(0);
    }

    [Fact]
    public async Task GetChatCounts_WithCutoff_FiltersByDate()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id);
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, user.Id, "Old message");

        var counts = await _repo.GetChatCountsAsync(chat.Id, DateTime.UtcNow.AddDays(1));

        counts.MediaCount.Should().Be(0);
        counts.FilesCount.Should().Be(0);
    }
}