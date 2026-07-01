using API.Domain.Entities;
using API.Infrastructure.Database;
using Shared.Enum;

namespace API.Tests.Integration.Infrastructure;

public static class TestDataSeeder
{
    public static async Task<User> SeedUserAsync(
        MessengerDbContext context,
        string username = "testuser",
        string password = "Password123!",
        bool isBanned = false,
        int? departmentId = null)
    {
        var user = new User
        {
            Username = username,
            IsBanned = isBanned,
            DepartmentId = departmentId,
            Password = new UserPassword(),
            UserSetting = new UserSetting { NotificationsEnabled = true }
        };
        user.Password.SetPassword(password);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public static async Task<Chat> SeedChatAsync(
        MessengerDbContext context,
        int creatorId,
        ChatType type = ChatType.Chat,
        string? name = "Test Chat",
        int[]? memberIds = null)
    {
        var chat = new Chat
        {
            Name = type == ChatType.Contact ? null : name,
            Type = type,
            CreatedById = creatorId,
            CreatedAt = DateTime.UtcNow,
            ShowHistoryForNewMembers = true
        };
        context.Chats.Add(chat);
        await context.SaveChangesAsync();

        var allMembers = new List<int> { creatorId };
        if (memberIds is not null)
            allMembers.AddRange(memberIds.Where(id => id != creatorId));

        foreach (var uid in allMembers)
        {
            context.ChatMembers.Add(new ChatMember
            {
                ChatId = chat.Id,
                UserId = uid,
                Role = uid == creatorId ? ChatRole.Owner : ChatRole.Member,
                JoinedAt = DateTime.UtcNow,
                NotificationsEnabled = true
            });
        }
        await context.SaveChangesAsync();
        return chat;
    }

    public static Task<Chat> SeedContactChatAsync(
        MessengerDbContext context,
        int user1Id,
        int user2Id)
        => SeedChatAsync(context, user1Id, ChatType.Contact, null, [user1Id, user2Id]);

    public static Task<Chat> SeedGroupChatAsync(
        MessengerDbContext context,
        int creatorId,
        string name,
        params int[] memberIds)
        => SeedChatAsync(context, creatorId, ChatType.Chat, name, memberIds);

    public static async Task<UserMessage> SeedMessageAsync(
        MessengerDbContext context,
        int chatId,
        int senderId,
        string content = "Test message",
        int? replyToId = null,
        int? forwardFromId = null)
    {
        var message = new UserMessage
        {
            ChatId = chatId,
            SenderId = senderId,
            Content = content,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };
        if (replyToId.HasValue) message.ReplyToMessageId = replyToId;
        if (forwardFromId.HasValue) message.ForwardedFromMessageId = forwardFromId;
        context.UserMessages.Add(message);
        await context.SaveChangesAsync();
        return message;
    }

    public static async Task<(Poll Poll, UserMessage Message)> SeedPollAsync(
        MessengerDbContext context,
        string question = "Test poll?",
        string[]? options = null)
    {
        var user = await SeedUserAsync(context, $"poll_creator_{Guid.NewGuid():N}", "pass");
        var chat = await SeedChatAsync(context, user.Id);
        var msg = await SeedMessageAsync(context, chat.Id, user.Id, question);

        var poll = new Poll
        {
            MessageId = msg.Id,
            IsAnonymous = false,
            AllowsMultipleAnswers = false
        };
        context.Polls.Add(poll);
        await context.SaveChangesAsync();

        options ??= ["Option A", "Option B"];
        for (int i = 0; i < options.Length; i++)
            context.PollOptions.Add(new PollOption
            {
                PollId = poll.Id,
                OptionText = options[i],
                Position = i
            });
        await context.SaveChangesAsync();

        return (poll, msg);
    }
}