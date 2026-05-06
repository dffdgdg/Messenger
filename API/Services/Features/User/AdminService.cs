using API.Services.Base;

namespace API.Services.User;

public partial class AdminService(MessengerDbContext context, AppDateTime appDateTime, ILogger<AdminService> logger)
    : BaseService<AdminService>(context, logger), IAdminService
{
    public async Task<Result<List<UserDto>>> GetUsersAsync(CancellationToken ct = default)
    {
        var users = await ProjectToDto(_context.Users).OrderBy(u => u.Surname).ThenBy(u => u.Name).AsNoTracking().ToListAsync(ct);
        return Result<List<UserDto>>.Success(users);
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

        var username = dto.Username!.Trim().ToLowerInvariant();

        var exists = await _context.Users.AnyAsync(u => u.Username == username, ct);
        if (exists)
            return Result<UserDto>.Conflict("Пользователь с таким логином уже существует");

        if (dto.DepartmentId.HasValue)
        {
            var deptExists = await _context.Departments.AnyAsync(d => d.Id == dto.DepartmentId.Value, ct);
            if (!deptExists)
                return Result<UserDto>.NotFound("Указанный отдел не существует");
        }

        var user = new Data.User
        {
            Username = username,
            Password = new UserPassword(),
            Surname = dto.Surname.Trim(),
            Name = dto.Name.Trim(),
            Midname = dto.Midname?.Trim(),
            DepartmentId = dto.DepartmentId,
            CreatedAt = appDateTime.UtcNow,
            IsBanned = false,
            UserSetting = new UserSetting
            {
                NotificationsEnabled = true,
                Theme = 0,
            },
        };

        _context.Users.Add(user);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<UserDto>();

        LogUserCreated(username, user.Id);

        var created = await ProjectToDto(_context.Users.Where(u => u.Id == user.Id)).AsNoTracking().FirstAsync(ct);

        return Result<UserDto>.Success(created);
    }

    public async Task<Result<UserDto>> UpdateUserAsync(int userId, UserDto dto, CancellationToken ct = default)
    {
        if (userId != dto.Id)
            return Result<UserDto>.Failure("Несоответствие идентификатора");

        var usernameValidation = ValidationHelper.ValidateUsername(dto.Username);
        if (usernameValidation.IsFailure)
            return Result<UserDto>.Failure(usernameValidation.Error!);

        if (string.IsNullOrWhiteSpace(dto.Surname))
            return Result<UserDto>.Failure("Фамилия не может быть пустой");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result<UserDto>.Failure("Имя не может быть пустым");

        var username = dto.Username!.Trim().ToLowerInvariant();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return Result<UserDto>.NotFound($"Пользователь с ID {userId} не найден");

        var taken = await _context.Users.AnyAsync(u => u.Username == username && u.Id != userId, ct);
        if (taken)
            return Result<UserDto>.Conflict("Пользователь с таким логином уже существует");

        if (dto.DepartmentId.HasValue)
        {
            var deptExists = await _context.Departments.AnyAsync(d => d.Id == dto.DepartmentId.Value, ct);
            if (!deptExists)
                return Result<UserDto>.NotFound("Указанный отдел не существует");
        }

        user.Username = username;
        user.Surname = dto.Surname.Trim();
        user.Name = dto.Name.Trim();
        user.Midname = dto.Midname?.Trim();
        user.DepartmentId = dto.DepartmentId;

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<UserDto>();

        LogUserUpdated(userId);

        var updated = await ProjectToDto(_context.Users.Where(u => u.Id == userId)).AsNoTracking().FirstAsync(ct);

        return Result<UserDto>.Success(updated);
    }

    public async Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default)
    {
        var userResult = await FindEntityAsync<Data.User>(userId, ct);
        if (userResult.IsFailure) return userResult;

        var user = userResult.Value!;
        user.IsBanned = !user.IsBanned;

        if (user.IsBanned)
        {
            await _context.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, appDateTime.UtcNow), ct);
        }

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogBanStatusChanged(userId, user.IsBanned ? "заблокирован" : "разблокирован");
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        var passwordValidation = ValidationHelper.ValidatePassword(newPassword);
        if (passwordValidation.IsFailure)
            return Result.Failure(passwordValidation.Error!);

        var user = await _context.Users.Include(u => u.Password).FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return Result.NotFound($"Пользователь с ID {userId} не найден");

        user.Password.SetPassword(newPassword);

        await _context.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, appDateTime.UtcNow), ct);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogPasswordReset(userId);
        return Result.Success();
    }

    private static IQueryable<UserDto> ProjectToDto(IQueryable<Data.User> query) => query.Select(u => new UserDto
    {
        Id = u.Id,
        Username = u.Username,
        DisplayName = (u.Surname + " " + u.Name + (u.Midname != null ? " " + u.Midname : "")).Trim(),
        Surname = u.Surname,
        Name = u.Name,
        Midname = u.Midname,
        Avatar = u.Avatar,
        DepartmentId = u.DepartmentId,
        Department = u.Department != null ? u.Department.Name : null,
        IsBanned = u.IsBanned,
        LastOnline = u.LastOnline,
        Theme = u.UserSetting != null ? u.UserSetting.Theme : null,
        NotificationsEnabled = u.UserSetting == null || u.UserSetting.NotificationsEnabled
    });

    #region Log
    [LoggerMessage(Level = LogLevel.Information, Message = "Создан пользователь {Username} (ID={UserId})")]
    private partial void LogUserCreated(string username, int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Администратор обновил профиль пользователя ID={UserId}")]
    private partial void LogUserUpdated(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Администратор сбросил пароль пользователя ID={UserId}")]
    private partial void LogPasswordReset(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь ID={UserId} {Action}", EventName = "UserBanStatusChanged")]
    private partial void LogBanStatusChanged(int userId, string action);
    #endregion
}