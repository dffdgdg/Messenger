using MessengerAPI.Common;
using MessengerAPI.Mapping;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerShared.DTO.User;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services.User;

public interface IAdminService
{
    Task<Result<List<UserDTO>>> GetUsersAsync(CancellationToken ct = default);
    Task<Result<UserDTO>> CreateUserAsync(CreateUserDTO dto, CancellationToken ct = default);
    Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default);
}

public class AdminService(
    MessengerDbContext context,
    ILogger<AdminService> logger)
    : BaseService<AdminService>(context, logger), IAdminService
{
    public async Task<Result<List<UserDTO>>> GetUsersAsync(CancellationToken ct = default)
    {
        var users = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.UserSetting)
            .AsNoTracking()
            .ToListAsync(ct);

        var result = users.ConvertAll(u => u.ToDto());
        return Result<List<UserDTO>>.Success(result);
    }

    public async Task<Result<UserDTO>> CreateUserAsync(CreateUserDTO dto, CancellationToken ct = default)
    {
        var usernameValidation = ValidationHelper.ValidateUsername(dto.Username);
        if (usernameValidation.IsFailure)
            return Result<UserDTO>.Failure(usernameValidation.Error!);

        var passwordValidation = ValidationHelper.ValidatePassword(dto.Password);
        if (passwordValidation.IsFailure)
            return Result<UserDTO>.Failure(passwordValidation.Error!);

        if (string.IsNullOrWhiteSpace(dto.Surname))
            return Result<UserDTO>.Failure("Фамилия не может быть пустой");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result<UserDTO>.Failure("Имя не может быть пустым");

        var username = dto.Username!.Trim().ToLower();

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
            IsBanned = false,
            UserSetting = new UserSetting
            {
                NotificationsEnabled = true,
                Theme = 0
            }
        };

        _context.Users.Add(user);
        await SaveChangesAsync(ct);

        _logger.LogInformation("Создан пользователь {Username} с ID {UserId}",
            username, user.Id);

        var createdUser = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.UserSetting)
            .AsNoTracking()
            .FirstAsync(u => u.Id == user.Id, ct);

        return Result<UserDTO>.Success(createdUser.ToDto());
    }

    public async Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default)
    {
        var user = await _context.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Failure($"Пользователь с ID {userId} не найден");

        user.IsBanned = !user.IsBanned;
        await SaveChangesAsync(ct);

        _logger.LogInformation("Пользователь {UserId} {Action}", userId, user.IsBanned ? "заблокирован" : "разблокирован");

        return Result.Success();
    }
}