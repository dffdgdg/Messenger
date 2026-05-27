using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace API.Data.SeedData;

public class DataSeeder(MessengerDbContext db, ILogger<DataSeeder> logger, IConfiguration configuration)
{
    private readonly int _bcryptWorkFactor = configuration.GetValue("MessengerSettings:BcryptWorkFactor", 12);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SeedAsync()
    {
        var count = await db.Users.CountAsync();

        var ids = await db.Users.Select(u => u.Id).Take(10).ToListAsync();

        var userDtos = await LoadUserDtosAsync();

        await UpdatePasswordsAsync();
    }
    /// <summary>
    /// Обновляет пароли у всех пользователей из users.json.
    /// Остальные данные не трогает — они уже есть в БД.
    /// </summary>
    private async Task UpdatePasswordsAsync()
    {
        logger.LogInformation("🔐 Обновление паролей пользователей...");

        var userDtos = await LoadUserDtosAsync();

        if (userDtos.Count == 0)
        {
            logger.LogWarning("⚠️ Файл users.json пуст или не найден");
            return;
        }

        var userIds = userDtos.Select(u => u.Id).ToList();

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Include(u => u.Password)
            .ToListAsync();

        if (users.Count == 0)
        {
            logger.LogWarning("Пользователи не найдены в БД. Убедитесь, что данные уже засеяны");
            return;
        }

        var dtoById = userDtos.ToDictionary(u => u.Id);

        int updated = 0;

        foreach (var user in users)
        {
            if (!dtoById.TryGetValue(user.Id, out var dto))
                continue;

            var newHash = BCrypt.Net.BCrypt.HashPassword(dto.PasswordPlain, _bcryptWorkFactor);

            if (user.Password is null)
            {
                user.Password = new UserPassword { Hash = newHash };
            }
            else
            {
                user.Password.Hash = newHash;
            }

            updated++;
        }

        await db.SaveChangesAsync();
    }

    private async Task<List<UserSeedDto>> LoadUserDtosAsync()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "SeedData", "users.json");

        if (!File.Exists(path))
        {
            logger.LogError("Файл не найден: {Path}", path);
            return [];
        }
        var jsonData = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<UserSeedDto>>(jsonData, _jsonOptions) ?? [];
    }
}

public class UserSeedDto
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Surname { get; set; } = null!;
    public string? Midname { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? DepartmentId { get; set; }
    public string? Avatar { get; set; }
    public UserStatusType StatusType { get; set; }
    public string PasswordPlain { get; set; } = null!;
}