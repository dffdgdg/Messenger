using MessengerAPI.Common;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerShared.DTO.User;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace MessengerAPI.Services.User
{
    public interface IAdminService
    {
        Task<Result<List<UserDTO>>> GetUsersAsync(CancellationToken ct = default);
        Task<Result<UserDTO>> CreateUserAsync(CreateUserDTO dto, CancellationToken ct = default);
        Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default);
    }

    public class AdminService(MessengerDbContext context,ILogger<AdminService> logger) : BaseService<AdminService>(context, logger), IAdminService
    {
        public async Task<Result<List<UserDTO>>> GetUsersAsync(CancellationToken ct = default)
        {
            var users = await _context.Users.Include(u => u.Department).Include(u => u.UserSetting).AsNoTracking().ToListAsync(ct);

            var result = users.ConvertAll(MapToDto);
            return Result<List<UserDTO>>.Success(result);
        }

        public async Task<Result<UserDTO>> CreateUserAsync(
            CreateUserDTO dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Username))
                return Result<UserDTO>.Failure("Логин не может быть пустым");

            if (string.IsNullOrWhiteSpace(dto.Password))
                return Result<UserDTO>.Failure("Пароль не может быть пустым");

            if (dto.Password.Length < 6)
                return Result<UserDTO>.Failure("Пароль должен содержать минимум 6 символов");

            if (string.IsNullOrWhiteSpace(dto.Surname))
                return Result<UserDTO>.Failure("Фамилия не может быть пустой");

            if (string.IsNullOrWhiteSpace(dto.Name))
                return Result<UserDTO>.Failure("Имя не может быть пустым");

            var username = dto.Username.Trim().ToLower();

            if (!Regex.IsMatch(username, "^[a-z0-9_]{3,30}$"))
                return Result<UserDTO>.Failure("Логин должен содержать 3-30 символов (латинские буквы, цифры, подчёркивания)");

            var exists = await _context.Users.AnyAsync(u => u.Username == username, ct);

            if (exists)
                return Result<UserDTO>.Failure("Пользователь с таким логином уже существует");

            if (dto.DepartmentId.HasValue)
            {
                var departmentExists = await _context.Departments
                    .AnyAsync(d => d.Id == dto.DepartmentId.Value, ct);
                if (!departmentExists)
                    return Result<UserDTO>.Failure("Указанный отдел не существует");
            }

            var user = new Model.User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Surname = dto.Surname.Trim(),
                Name = dto.Name.Trim(),
                Midname = dto.Midname?.Trim(),
                DepartmentId = dto.DepartmentId,
                CreatedAt = AppDateTime.UtcNow,
                IsBanned = false
            };

            _context.Users.Add(user);
            await SaveChangesAsync(ct);

            var userSettings = new UserSetting
            {
                UserId = user.Id,
                NotificationsEnabled = true,
                Theme = 0
            };

            _context.UserSettings.Add(userSettings);
            await SaveChangesAsync(ct);

            _logger.LogInformation("Создан пользователь {Username} с ID {UserId}",username, user.Id);

            var createdUser = await _context.Users
                .Include(u => u.Department)
                .Include(u => u.UserSetting)
                .FirstAsync(u => u.Id == user.Id, ct);

            return Result<UserDTO>.Success(MapToDto(createdUser));
        }

        public async Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default)
        {
            var user = await _context.Users.FindAsync([userId], ct);
            if (user == null)
                return Result.Failure($"Пользователь с ID {userId} не найден");

            user.IsBanned = !user.IsBanned;
            await SaveChangesAsync(ct);

            _logger.LogInformation("Пользователь {UserId} {Action}",userId, user.IsBanned ? "заблокирован" : "разблокирован");

            return Result.Success();
        }

        private static UserDTO MapToDto(Model.User user) => new()
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

        private static string FormatDisplayName(Model.User user)
        {
            var parts = new[] { user.Surname, user.Name, user.Midname }.Where(p => !string.IsNullOrWhiteSpace(p));
            return string.Join(" ", parts);
        }
    }
}