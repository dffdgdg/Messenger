using API.Services.Base;
using API.Services.Infrastructure.Bundles;

namespace API.Services.User;

public partial class UserService(MessengerDbContext context, MediaBundle media, PresenceBundle presence, UrlBundle url, ILogger<UserService> logger)
    : BaseService<UserService>(context, logger), IUserService
{
    private readonly IFileService fileService = media.FileService;
    private readonly IOnlineUserService onlineService = presence.OnlineService;
    private readonly IUrlBuilder urlBuilder = url.UrlBuilder;

    public async Task<Result<List<UserDto>>> GetAllUsersAsync(CancellationToken ct = default)
    {
        var users = await _context.Users.Select(u => new
        {
            User = u,
            DepartmentName = u.Department != null ? u.Department.Name : null,
            Theme = u.UserSetting != null ? u.UserSetting.Theme : null,
            NotificationsEnabled = u.UserSetting == null || u.UserSetting.NotificationsEnabled
        }).AsNoTracking().ToListAsync(ct);

        var onlineIds = onlineService.GetOnlineUserIds();

        var result = users.ConvertAll(u =>
        {
            var dto = u.User.ToDto(urlBuilder, onlineIds.Contains(u.User.Id));
            dto.Theme = u.Theme;
            dto.NotificationsEnabled = u.NotificationsEnabled;
            return dto;
        });

        return Result<List<UserDto>>.Success(result);
    }

    public async Task<Result<UserDto>> GetUserAsync(int id, CancellationToken ct = default)
    {
        var user = await _context.Users.Select(u => new
        {
            User = u,
            Theme = u.UserSetting != null ? u.UserSetting.Theme : null,
            NotificationsEnabled = u.UserSetting == null || u.UserSetting.NotificationsEnabled,
            DepartmentName = u.Department != null ? u.Department.Name : null
        }).AsNoTracking().FirstOrDefaultAsync(u => u.User.Id == id, ct);

        if (user is null)
            return Result<UserDto>.NotFound($"Пользователь с ID {id} не найден");

        var dto = user.User.ToDto(urlBuilder, onlineService.IsOnline(id));
        dto.Theme = user.Theme;
        dto.NotificationsEnabled = user.NotificationsEnabled;

        return Result<UserDto>.Success(dto);
    }

    public async Task<Result> UpdateUserAsync(int id, UserDto dto, CancellationToken ct = default)
    {
        if (id != dto.Id)
            return Result.Failure("Несоответствие ID");

        var user = await _context.Users.Include(u => u.UserSetting).FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
            return Result.NotFound($"Пользователь с ID {id} не найден");

        user.UpdateProfile(dto);

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return saveResult;

        LogUserUpdated(id);
        return Result.Success();
    }

    public async Task<Result<AvatarResponseDto>> UploadAvatarAsync(int id, IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return Result<AvatarResponseDto>.Failure("Файл не предоставлен");

        var userResult = await FindEntityAsync<Data.User>(id, ct);
        if (userResult.IsFailure) return userResult.As<AvatarResponseDto>();

        var user = userResult.Value!;

        var saveResult = await fileService.SaveImageAsync(file, "avatars/users", user.Avatar);
        if (saveResult.IsFailure) return saveResult.As<AvatarResponseDto>();

        user.Avatar = saveResult.Value;

        var dbSave = await SaveChangesAsync(ct);
        if (dbSave.IsFailure) return dbSave.As<AvatarResponseDto>();

        LogAvatarUpdated(id);

        return Result<AvatarResponseDto>.Success(new AvatarResponseDto
        {
            AvatarUrl = urlBuilder.BuildUrl(saveResult.Value)!
        });
    }

    public Task<Result<OnlineUsersResponseDto>> GetOnlineUsersAsync(CancellationToken ct = default)
    {
        var onlineIds = onlineService.GetOnlineUserIds();
        return Task.FromResult(Result<OnlineUsersResponseDto>.Success(new OnlineUsersResponseDto
        {
            OnlineUserIds = [.. onlineIds],
            TotalOnline = onlineIds.Count
        }));
    }

    public async Task<Result<UserStatusDto>> GetOnlineStatusAsync(int userId, CancellationToken ct = default)
    {
        var user = await _context.Users.AsNoTracking().Select(u => new { u.Id, u.LastOnline, u.StatusType, u.StatusExpiresAt })
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        var isOnline = onlineService.IsOnline(userId);

        return Result<UserStatusDto>.Success(new UserStatusDto(userId, isOnline, user?.LastOnline, isOnline ? user?.StatusType
            ?? UserStatusType.Online : UserStatusType.Online, user?.StatusExpiresAt));
    }

    public async Task<Result<List<UserStatusDto>>> GetOnlineStatusesAsync(List<int> userIds, CancellationToken ct = default)
    {
        if (userIds is null || userIds.Count == 0)
            return Result<List<UserStatusDto>>.Failure("Список ID пользователей не может быть пустым");

        var users = await _context.Users.Where(u => userIds.Contains(u.Id)).AsNoTracking().Select(u => new { u.Id, u.LastOnline }).ToListAsync(ct);

        var onlineIds = onlineService.FilterOnline(userIds);

        var result = users.ConvertAll(u => new UserStatusDto(u.Id, onlineIds.Contains(u.Id), u.LastOnline));

        return Result<List<UserStatusDto>>.Success(result);
    }

    public async Task<Result> ChangeUsernameAsync(int id, ChangeUsernameDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.NewUsername))
            return Result.Failure("Username не может быть пустым");

        var username = dto.NewUsername.Trim().ToLower();

        var validation = ValidationHelper.ValidateUsername(dto.NewUsername);
        if (validation.IsFailure) return validation;

        var exists = await _context.Users.AnyAsync(u => u.Username == username && u.Id != id, ct);
        if (exists)
            return Result.Conflict("Этот username уже занят");

        var userResult = await FindEntityAsync<Data.User>(id, ct);
        if (userResult.IsFailure) return userResult;

        userResult.Value!.Username = username;

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogUsernameChanged(id);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(int id, ChangePasswordDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
            return Result.Failure("Введите текущий пароль");

        if (string.IsNullOrWhiteSpace(dto.NewPassword))
            return Result.Failure("Введите новый пароль");

        if (dto.NewPassword.Length < 6)
            return Result.Failure("Пароль должен содержать минимум 6 символов");

        var user = await _context.Users.Include(u => u.Password).FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
            return Result.NotFound($"Пользователь с ID {id} не найден");

        if (!user.Password.Verify(dto.CurrentPassword))
            return Result.Unauthorized("Неверный текущий пароль");

        user.Password.SetPassword(dto.NewPassword);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogPasswordChanged(id);
        return Result.Success();
    }

    #region Log messages

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} обновлён")]
    private partial void LogUserUpdated(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Аватар обновлён для пользователя {UserId}")]
    private partial void LogAvatarUpdated(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Username изменён для пользователя {UserId}")]
    private partial void LogUsernameChanged(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пароль изменён для пользователя {UserId}")]
    private partial void LogPasswordChanged(int userId);

    #endregion
}