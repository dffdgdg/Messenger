using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IAdminService
    {
        Task<List<UserDTO>> GetUsersAsync(CancellationToken ct = default);
        Task<UserDTO> CreateUserAsync(CreateUserDTO dto, CancellationToken ct = default);
        Task ToggleBanAsync(int userId, CancellationToken ct = default);
    }

    public class AdminService(MessengerDbContext context, ILogger<AdminService> logger)
        : BaseService<AdminService>(context, logger), IAdminService
    {
        public async Task<List<UserDTO>> GetUsersAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("Получение списка всех пользователей");

            var users = await _context.Users.Include(u => u.Department).Include(u => u.UserSetting)
                .AsNoTracking().ToListAsync(ct);

            var result = users.Select(MapToDto).ToList();

            _logger.LogDebug("Получено {Count} пользователей", result.Count);

            return result;
        }

        public async Task<UserDTO> CreateUserAsync(CreateUserDTO dto, CancellationToken ct = default)
        {
            _logger.LogDebug("Создание пользователя {Username}", dto.Username);

            // Валидация
            if (string.IsNullOrWhiteSpace(dto.Username))
                throw new ArgumentException("Логин не может быть пустым");

            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new ArgumentException("Пароль не может быть пустым");

            if (dto.Password.Length < 6)
                throw new ArgumentException("Пароль должен содержать минимум 6 символов");

            if (string.IsNullOrWhiteSpace(dto.Surname))
                throw new ArgumentException("Фамилия не может быть пустой");

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Имя не может быть пустым");

            var username = dto.Username.Trim().ToLower();

            // Проверка формата username
            if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-z0-9_]{3,30}$"))
                throw new ArgumentException("Логин должен содержать 3-30 символов (латинские буквы, цифры, подчёркивания)");

            // Проверка уникальности
            var exists = await _context.Users
                .AnyAsync(u => u.Username.ToLower() == username, ct);

            if (exists)
                throw new ArgumentException("Пользователь с таким логином уже существует");

            // Проверка отдела
            if (dto.DepartmentId.HasValue)
            {
                var departmentExists = await _context.Departments
                    .AnyAsync(d => d.Id == dto.DepartmentId.Value, ct);

                if (!departmentExists)
                    throw new ArgumentException("Указанный отдел не существует");
            }

            // Создание пользователя
            var user = new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Surname = dto.Surname.Trim(),
                Name = dto.Name.Trim(),
                Midname = dto.Midname?.Trim(),
                DepartmentId = dto.DepartmentId,
                CreatedAt = DateTime.UtcNow,
                IsBanned = false
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(ct);

            // Создаём настройки пользователя
            var userSettings = new UserSetting
            {
                UserId = user.Id,
                NotificationsEnabled = true,
                Theme = 0
            };

            _context.UserSettings.Add(userSettings);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Создан пользователь {Username} с ID {UserId}", username, user.Id);

            // Перезагружаем с навигационными свойствами
            var createdUser = await _context.Users
                .Include(u => u.Departments)
                .Include(u => u.UserSetting)
                .FirstAsync(u => u.Id == user.Id, ct);

            return MapToDto(createdUser);
        }

        public async Task ToggleBanAsync(int userId, CancellationToken ct = default)
        {
            var user = await _context.Users.FindAsync([userId], ct);

            if (user == null)
                throw new KeyNotFoundException($"Пользователь с ID {userId} не найден");

            user.IsBanned = !user.IsBanned;

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Пользователь {UserId} {Action}",
                userId, user.IsBanned ? "заблокирован" : "разблокирован");
        }

        private static UserDTO MapToDto(User user) => new()
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = FormatDisplayName(user),
            Name = user.Name,
            Surname = user.Surname,
            Midname = user.Midname,
            Department = user.Department?.Name,
            DepartmentId = user.DepartmentId,
            Avatar = user.Avatar,
            IsBanned = user.IsBanned,
            NotificationsEnabled = user.UserSetting?.NotificationsEnabled ?? true,
            Theme = user.UserSetting?.Theme ?? 0
        };

        private static string FormatDisplayName(User user)
        {
            var parts = new[] { user.Surname, user.Name, user.Midname }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            return string.Join(" ", parts);
        }
    }
}