using System.Security.Claims;

namespace Messenger.Tests.Helpers;

public static class TestHelpers
{
    public static void SetupControllerContext(ControllerBase controller, int userId, string role = "User")
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Name, "testuser"), new(ClaimTypes.Role, role) };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    public static Mock<ILogger<T>> CreateLogger<T>() => new();

    public static MessengerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MessengerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new MessengerDbContext(options);
    }

    public static async Task<MessengerDbContext> CreateSeededDbContext()
    {
        var context = CreateDbContext();

        // Отделы
        context.Departments.AddRange(
            new Department { Id = 1, Name = "IT" },
            new Department { Id = 2, Name = "Администрация" }
        );

        // Пользователи
        context.Users.AddRange(
            new User
            {
                Id = 1,
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                Name = "Admin",
                Surname = "User",
                DepartmentId = 2
            },
            new User
            {
                Id = 2,
                Username = "user1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("user123"),
                Name = "Test",
                Surname = "User",
                DepartmentId = 1
            }
        );

        // Настройки
        context.UserSettings.AddRange(new UserSetting { UserId = 1, NotificationsEnabled = true },new UserSetting { UserId = 2, NotificationsEnabled = true });

        await context.SaveChangesAsync();
        return context;
    }
}