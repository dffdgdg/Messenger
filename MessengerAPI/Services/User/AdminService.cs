using MessengerAPI.Services.Base;

namespace MessengerAPI.Services.User;

public interface IAdminService
{
    Task<Result<List<UserDto>>> GetUsersAsync(CancellationToken ct = default);
    Task<Result<UserDto>> CreateUserAsync(CreateUserDto dto, CancellationToken ct = default);
    Task<Result<UserDto>> UpdateUserAsync(int userId, UserDto dto, CancellationToken ct = default);
    Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default);
}

public class AdminService(
    MessengerDbContext context,
    AppDateTime appDateTime,
    ILogger<AdminService> logger)
    : BaseService<AdminService>(context, logger), IAdminService
{
    public async Task<Result<List<UserDto>>> GetUsersAsync(CancellationToken ct = default)
    {
        var users = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.UserSetting)
            .AsNoTracking()
            .ToListAsync(ct);

        var result = users.ConvertAll(u => u.ToDto());
        return Result<List<UserDto>>.Success(result);
    }

    public async Task<Result<UserDto>> CreateUserAsync(CreateUserDto dto, CancellationToken ct = default)
    {
        var usernameValidation = ValidationHelper.ValidateUsername(dto.Username);
        if (usernameValidation.IsFailure)
            return Result<UserDto>.Failure(usernameValidation.Error!);

        var passwordValidation = ValidationHelper.ValidatePassword(dto.Password);
        if (passwordValidation.IsFailure)
            return Result<UserDto>.Failure(passwordValidation.Error!);

        if (string.IsNullOrWhiteSpace(dto.Surname))
            return Result<UserDto>.Failure("Фамилия не может быть пустой");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result<UserDto>.Failure("Имя не может быть пустым");

        var username = dto.Username!.Trim().ToLower();

        var exists = await _context.Users.AnyAsync(u => u.Username == username, ct);
        if (exists)
            return Result<UserDto>.Conflict("Пользователь с таким логином уже существует");

        if (dto.DepartmentId.HasValue)
        {
            var departmentExists = await _context.Departments.AnyAsync(d => d.Id == dto.DepartmentId.Value, ct);
            if (!departmentExists)
                return Result<UserDto>.NotFound("Указанный отдел не существует");
        }

        var user = new Model.User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Surname = dto.Surname.Trim(),
            Name = dto.Name.Trim(),
            Midname = dto.Midname?.Trim(),
            DepartmentId = dto.DepartmentId,
            CreatedAt = appDateTime.UtcNow,
            IsBanned = false,
            UserSetting = new UserSetting
            {
                NotificationsEnabled = true,
                Theme = 0
            }
        };

        _context.Users.Add(user);

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return Result<UserDto>.FromFailure(saveResult);

        _logger.LogInformation("Создан пользователь {Username} с ID {UserId}", username, user.Id);

        var createdUser = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.UserSetting)
            .AsNoTracking()
            .FirstAsync(u => u.Id == user.Id, ct);

        return Result<UserDto>.Success(createdUser.ToDto());
    }

    public async Task<Result<UserDto>> UpdateUserAsync(int userId, UserDto dto, CancellationToken ct = default)
    {
        if (userId != dto.Id)
            return Result<UserDto>.Failure("Несоответствие ID");

        var usernameValidation = ValidationHelper.ValidateUsername(dto.Username);
        if (usernameValidation.IsFailure)
            return Result<UserDto>.Failure(usernameValidation.Error!);

        if (string.IsNullOrWhiteSpace(dto.Surname))
            return Result<UserDto>.Failure("Фамилия не может быть пустой");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result<UserDto>.Failure("Имя не может быть пустым");

        var username = dto.Username!.Trim().ToLower();

        var user = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.UserSetting)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return Result<UserDto>.NotFound($"Пользователь с ID {userId} не найден");

        var exists = await _context.Users.AnyAsync(u => u.Username == username && u.Id != userId, ct);
        if (exists)
            return Result<UserDto>.Conflict("Пользователь с таким логином уже существует");

        if (dto.DepartmentId.HasValue)
        {
            var departmentExists = await _context.Departments.AnyAsync(d => d.Id == dto.DepartmentId.Value, ct);
            if (!departmentExists)
                return Result<UserDto>.NotFound("Указанный отдел не существует");
        }

        user.Username = username;
        user.UpdateProfile(dto);
        user.DepartmentId = dto.DepartmentId;

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return Result<UserDto>.FromFailure(saveResult);

        _logger.LogInformation("Администратор обновил пользователя {UserId}", userId);

        var updatedUser = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.UserSetting)
            .AsNoTracking()
            .FirstAsync(u => u.Id == userId, ct);

        return Result<UserDto>.Success(updatedUser.ToDto());
    }

    public async Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default)
    {
        var userResult = await FindEntityAsync<Model.User>(userId, ct);
        if (userResult.IsFailure)
            return Result.FromFailure(userResult);

        var user = userResult.Value!;
        user.IsBanned = !user.IsBanned;

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return saveResult;

        _logger.LogInformation("Пользователь {UserId} {Action}", userId, user.IsBanned ? "заблокирован" : "разблокирован");

        return Result.Success();
    }
}