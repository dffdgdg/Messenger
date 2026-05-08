using API.Data;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Helpers;

public static class DbContextFactory
{
    /// <summary>
    /// Создаёт изолированный InMemory-контекст для каждого теста.
    /// Имя БД уникально, чтобы тесты не делили состояние.
    /// </summary>
    public static MessengerDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<MessengerDbContext>().UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString()).Options;
        return new MessengerDbContext(options);
    }

    /// <summary>
    /// Создаёт пользователя с хэшированным паролем и сохраняет в контекст.
    /// </summary>
    public static async Task<User> SeedUserAsync(MessengerDbContext context, string username = "testuser", string password = "password123", bool isBanned = false, int? departmentId = null)
    {
        var user = new User
        {
            Username = username,
            IsBanned = isBanned,
            DepartmentId = departmentId,
            Password = new UserPassword()
        };
        user.Password.SetPassword(password);

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Создаёт чат и добавляет участников.
    /// </summary>
    public static async Task<Chat> SeedChatAsync(MessengerDbContext context, int creatorId, ChatType type = ChatType.Chat, string name = "Test Chat", int[]? memberIds = null)
    {
        var chat = new Chat
        {
            Name = type == ChatType.Contact ? null : name,
            Type = type,
            CreatedById = creatorId,
            CreatedAt = DateTime.UtcNow
        };
        context.Chats.Add(chat);
        await context.SaveChangesAsync();

        var members = new List<int> { creatorId };
        if (memberIds is not null)
            members.AddRange(memberIds.Where(id => id != creatorId));

        foreach (var uid in members)
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
}