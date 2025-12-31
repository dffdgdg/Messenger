namespace Messenger.Tests.Helpers;

public static class DbContextFactory
{
    /// <summary>
    /// Создаёт InMemory контекст базы данных
    /// </summary>
    public static MessengerDbContext CreateInMemoryContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<MessengerDbContext>().UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString()).Options;

        return new MessengerDbContext(options);
    }

    /// <summary>
    /// Создаёт контекст с начальными данными
    /// </summary>
    public static async Task<MessengerDbContext> CreateSeededContextAsync(string? dbName = null)
    {
        var context = CreateInMemoryContext(dbName);
        await SeedDataAsync(context);
        return context;
    }

    private static async Task SeedDataAsync(MessengerDbContext context)
    {
        // Departments
        var departments = new List<Department>
        {
            new() { Id = 1, Name = "IT" },
            new() { Id = 2, Name = "Администрация" },
            new() { Id = 3, Name = "HR" }
        };
        context.Departments.AddRange(departments);

        // Users
        var users = new List<User>
        {
            new()
            {
                Id = 1,
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                Name = "Admin",
                Surname = "User",
                DepartmentId = 2,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = 2,
                Username = "user1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("user123"),
                Name = "Test",
                Surname = "User",
                DepartmentId = 1,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = 3,
                Username = "user2",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("user123"),
                Name = "Another",
                Surname = "User",
                DepartmentId = 1,
                CreatedAt = DateTime.UtcNow
            }
        };
        context.Users.AddRange(users);

        // User Settings
        var userSettings = users.Select(u => new UserSetting
        {
            UserId = u.Id,
            NotificationsEnabled = true,
            Theme = 0
        });
        context.UserSettings.AddRange(userSettings);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Добавляет тестовые чаты в контекст
    /// </summary>
    public static async Task SeedChatsAsync(MessengerDbContext context)
    {
        var chats = new List<Chat>
        {
            new()
            {
                Id = 1,
                Name = "Группа разработки",
                Type = ChatType.Chat,
                CreatedById = 1,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = 2,
                Name = null, // Диалог
                Type = ChatType.Contact,
                CreatedById = 1,
                CreatedAt = DateTime.UtcNow
            }
        };
        context.Chats.AddRange(chats);

        var chatMembers = new List<ChatMember>
        {
            new() { ChatId = 1, UserId = 1, Role = ChatRole.Owner, JoinedAt = DateTime.UtcNow },
            new() { ChatId = 1, UserId = 2, Role = ChatRole.Admin, JoinedAt = DateTime.UtcNow },
            new() { ChatId = 1, UserId = 3, Role = ChatRole.Member, JoinedAt = DateTime.UtcNow },
            new() { ChatId = 2, UserId = 1, Role = ChatRole.Owner, JoinedAt = DateTime.UtcNow },
            new() { ChatId = 2, UserId = 2, Role = ChatRole.Member, JoinedAt = DateTime.UtcNow }
        };
        context.ChatMembers.AddRange(chatMembers);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Добавляет тестовые сообщения в контекст
    /// </summary>
    public static async Task SeedMessagesAsync(MessengerDbContext context, int chatId = 1)
    {
        var messages = new List<Message>
        {
            new()
            {
                Id = 1,
                ChatId = chatId,
                SenderId = 1,
                Content = "Привет всем!",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            },
            new()
            {
                Id = 2,
                ChatId = chatId,
                SenderId = 2,
                Content = "Привет!",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new()
            {
                Id = 3,
                ChatId = chatId,
                SenderId = 1,
                Content = "Как дела?",
                CreatedAt = DateTime.UtcNow
            }
        };
        context.Messages.AddRange(messages);

        await context.SaveChangesAsync();
    }
}