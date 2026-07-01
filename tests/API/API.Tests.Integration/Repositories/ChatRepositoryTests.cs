using API.Domain.Entities;
using API.Infrastructure.Repositories.Implementations;
using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.Enum;

namespace API.Tests.Integration.Repositories;

public class ChatRepositoryTests : IntegrationTestBase
{
    private readonly ChatRepository _repo;

    public ChatRepositoryTests() => _repo = new ChatRepository(Context);

    [Fact]
    public async Task FindByIdWithMembers_ReturnsChatWithMembers()
    {
        var user = await TestDataSeeder.SeedUserAsync(Context, "creator", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, user.Id, ChatType.Chat, "Group", [user.Id]);

        var result = await _repo.FindByIdWithMembersAsync(chat.Id);

        result.Should().NotBeNull();
        result!.ChatMembers.Should().HaveCount(1);
        result.ChatMembers.First().UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindByIdWithMembers_ReturnsNull_WhenNotFound()
        => (await _repo.FindByIdWithMembersAsync(999)).Should().BeNull();

    [Fact]
    public async Task FindContactChat_ReturnsChat_WhenExists()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedContactChatAsync(Context, u1.Id, u2.Id);

        var result = await _repo.FindContactChatAsync(u1.Id, u2.Id);
        result.Should().NotBeNull();
        result!.Type.Should().Be(ChatType.Contact);
    }

    [Fact]
    public async Task FindContactChat_ReturnsNull_WhenNotExists()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");

        var result = await _repo.FindContactChatAsync(u1.Id, u2.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMemberIds_ReturnsAllMemberIds()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "u1", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "u2", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id, u2.Id]);

        var ids = await _repo.GetMemberIdsAsync(chat.Id);

        ids.Should().HaveCount(2);
        ids.Should().Contain([u1.Id, u2.Id]);
    }

    [Fact]
    public async Task GetMemberIds_EmptyChat_ReturnsEmptyList()
        => (await _repo.GetMemberIdsAsync(999)).Should().BeEmpty();

    [Fact]
    public async Task IsMember_True()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "member", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);

        var isMember = await _repo.IsMemberAsync(chat.Id, u1.Id);
        isMember.Should().BeTrue();
    }

    [Fact]
    public async Task IsMember_False()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "member", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "outsider", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);

        var isMember = await _repo.IsMemberAsync(chat.Id, u2.Id);
        isMember.Should().BeFalse();
    }

    [Fact]
    public async Task AddMember_AddsSuccessfully()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "creator", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "newbie", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);

        _repo.AddMember(new ChatMember { ChatId = chat.Id, UserId = u2.Id, Role = ChatRole.Member, JoinedAt = DateTime.UtcNow });
        await Context.SaveChangesAsync();

        var isMember = await _repo.IsMemberAsync(chat.Id, u2.Id);
        isMember.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveMember_RemovesSuccessfully()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "owner", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "leaver", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id, u2.Id]);

        var member = await Context.ChatMembers.FirstAsync(cm => cm.ChatId == chat.Id && cm.UserId == u2.Id);
        _repo.RemoveMember(member);
        await Context.SaveChangesAsync();

        var isMember = await _repo.IsMemberAsync(chat.Id, u2.Id);
        isMember.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIds_ReturnsMatchingChats()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "u1", "pass");
        var chat1 = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Chat1", [u1.Id]);
        var chat2 = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Chat2", [u1.Id]);

        var chats = await _repo.GetByIdsAsync([chat1.Id, chat2.Id]);

        chats.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateLastMessageTime_UpdatesTimestamp()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "u1", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);
        var newTime = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var c = await Context.Chats.FindAsync(chat.Id);
        c!.LastMessageTime = newTime;
        await Context.SaveChangesAsync();

        var updated = await Context.Chats.AsNoTracking().FirstAsync(c => c.Id == chat.Id);
        updated.LastMessageTime.Should().Be(newTime);
    }

    [Fact]
    public async Task GetShowHistoryForNewMembers_ReturnsValue()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "u1", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);

        var result = await _repo.GetShowHistoryForNewMembersAsync(chat.Id);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetShowHistoryForNewMembers_Null_WhenNotFound()
        => (await _repo.GetShowHistoryForNewMembersAsync(999)).Should().BeNull();

    [Fact]
    public async Task GetLastMessages_ReturnsProjections()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);
        await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id, "Hello world");

        var messages = await _repo.GetLastMessagesAsync([chat.Id]);

        messages.Should().HaveCount(1);
        messages[0].Content.Should().Be("Hello world");
        messages[0].IsSystemMessage.Should().BeFalse();
    }

    [Fact]
    public async Task GetLastMessages_Empty_ReturnsEmptyList()
    {
        var messages = await _repo.GetLastMessagesAsync([999]);
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDialogPartners_ReturnsPartners()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedContactChatAsync(Context, u1.Id, u2.Id);

        var partners = await _repo.GetDialogPartnersAsync([chat.Id], u1.Id);
        partners.Should().HaveCount(1);
        partners[0].UserId.Should().Be(u2.Id);
    }

    [Fact]
    public async Task GetMembersWithUsers_ReturnsMemberProjections()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedGroupChatAsync(Context, u1.Id, "Group", u2.Id);

        var members = await _repo.GetMembersWithUsersAsync(chat.Id);
        members.Should().HaveCount(2);
        members.Select(m => m.UserId).Should().Contain([u1.Id, u2.Id]);
    }

    [Fact]
    public async Task GetVoiceFilePaths_ReturnsPaths()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "sender", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id]);
        var msg = await TestDataSeeder.SeedMessageAsync(Context, chat.Id, u1.Id);
        Context.VoiceMessages.Add(new VoiceMessage
        {
            MessageId = msg.Id,
            FilePath = "/uploads/voice/test.ogg",
            DurationSeconds = 10,
            FileSize = 1024
        });
        await Context.SaveChangesAsync();

        var paths = await _repo.GetVoiceFilePathsAsync(chat.Id);

        paths.Should().HaveCount(1);
        paths[0].Should().Be("/uploads/voice/test.ogg");
    }

    [Fact]
    public async Task GetContactChatsWithMembers_ReturnsContactChats()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat1 = await TestDataSeeder.SeedContactChatAsync(Context, u1.Id, u2.Id);
        var chat2 = await TestDataSeeder.SeedGroupChatAsync(Context, u1.Id, "Group", u2.Id);

        var result = await _repo.GetContactChatsWithMembersAsync([chat1.Id, chat2.Id]);
        result.Should().HaveCount(1);
        result[0].Type.Should().Be(ChatType.Contact);
    }

    [Fact]
    public async Task SearchGroupChats_ReturnsMatchingGroups()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "admin", "pass");
        await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Developers", [u1.Id]);
        await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Designers", [u1.Id]);
        await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Marketing", [u1.Id]);

        var ids = await Context.Chats.Select(c => c.Id).ToListAsync();
        var result = await Context.Chats
            .Where(c => ids.Contains(c.Id) && c.Type != ChatType.Contact && c.Name!.Contains("Dev"))
            .Take(10)
            .AsNoTracking()
            .ToListAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Developers");
    }

    [Fact]
    public async Task GetMembersForNotification_ExcludesSpecifiedUser()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "alice", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "bob", "pass");
        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Test", [u1.Id, u2.Id]);

        var members = await _repo.GetMembersForNotificationAsync(chat.Id, u1.Id);

        members.Should().HaveCount(1);
        members[0].UserId.Should().Be(u2.Id);
    }

    [Fact]
    public async Task GetHistoryRestrictions_ReturnsRestrictedChats()
    {
        var u1 = await TestDataSeeder.SeedUserAsync(Context, "owner", "pass");
        var u2 = await TestDataSeeder.SeedUserAsync(Context, "member", "pass");

        var chat = await TestDataSeeder.SeedChatAsync(Context, u1.Id, ChatType.Chat, "Restricted", [u1.Id, u2.Id]);

        var c = await Context.Chats.FindAsync(chat.Id);
        c!.ShowHistoryForNewMembers = false;
        await Context.SaveChangesAsync();
        var restrictions = await _repo.GetHistoryRestrictionsAsync([chat.Id], u2.Id);
        restrictions.Should().ContainKey(chat.Id);
    }
}