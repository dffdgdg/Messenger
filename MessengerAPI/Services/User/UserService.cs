using MessengerAPI.Common;
using MessengerAPI.Mapping;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerAPI.Services.Infrastructure;
using MessengerAPI.Services.Messaging;
using MessengerShared.Dto.Online;
using MessengerShared.Dto.User;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace MessengerAPI.Services.User;

public interface IUserService
{
    Task<Result<List<UserDto>>> GetAllUsersAsync(CancellationToken ct = default);
    Task<Result<UserDto>> GetUserAsync(int id, CancellationToken ct = default);
    Task<Result> UpdateUserAsync(int id, UserDto dto, CancellationToken ct = default);
    Task<Result<AvatarResponseDto>> UploadAvatarAsync(int id, IFormFile file, CancellationToken ct = default);
    Task<Result<OnlineUsersResponseDto>> GetOnlineUsersAsync(CancellationToken ct = default);
    Task<Result<OnlineStatusDto>> GetOnlineStatusAsync(int userId, CancellationToken ct = default);
    Task<Result<List<OnlineStatusDto>>> GetOnlineStatusesAsync(List<int> userIds, CancellationToken ct = default);
    Task<Result> ChangeUsernameAsync(int id, ChangeUsernameDto dto, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(int id, ChangePasswordDto dto, CancellationToken ct = default);
}

public partial class UserService(
    MessengerDbContext context,
    IFileService fileService,
    IOnlineUserService onlineService,
    IUrlBuilder urlBuilder,
    ILogger<UserService> logger)
    : BaseService<UserService>(context, logger), IUserService
{
    [GeneratedRegex("^[a-z0-9_]{3,30}$")]
    private static partial Regex UsernameRegex();

    public async Task<Result<List<UserDto>>> GetAllUsersAsync(CancellationToken ct = default)
    {
        var users = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.UserSetting)
            .AsNoTracking()
            .ToListAsync(ct);

        var onlineIds = onlineService.GetOnlineUserIds();

        var result = users.ConvertAll(u =>
            u.ToDto(urlBuilder, onlineIds.Contains(u.Id)));

        return Result<List<UserDto>>.Success(result);
    }

    public async Task<Result<UserDto>> GetUserAsync(int id, CancellationToken ct = default)
    {
        var user = await _context.Users
            .Include(u => u.UserSetting)
            .Include(u => u.Department)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user == null)
            return Result<UserDto>.Failure($"Пользователь с ID {id} не найден");

        return Result<UserDto>.Success(
            user.ToDto(urlBuilder, onlineService.IsOnline(id)));
    }

    public async Task<Result> UpdateUserAsync(int id, UserDto dto, CancellationToken ct = default)
    {
        if (id != dto.Id)
            return Result.Failure("Несоответствие ID");

        var user = await _context.Users
            .Include(u => u.UserSetting)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user == null)
            return Result.Failure($"Пользователь с ID {id} не найден");

        user.UpdateProfile(dto);
        await SaveChangesAsync(ct);

        _logger.LogInformation("Пользователь {UserId} обновлён", id);
        return Result.Success();
    }

    public async Task<Result<AvatarResponseDto>> UploadAvatarAsync(int id, IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return Result<AvatarResponseDto>.Failure("Файл не предоставлен");

        var user = await _context.Users.FindAsync([id], ct);
        if (user is null)
            return Result<AvatarResponseDto>.Failure($"Пользователь с ID {id} не найден");

        var avatarPath = await fileService.SaveImageAsync(file, "avatars/users", user.Avatar);
        user.Avatar = avatarPath;
        await SaveChangesAsync(ct);

        _logger.LogInformation("Аватар обновлён для пользователя {UserId}", id);

        return Result<AvatarResponseDto>.Success(new AvatarResponseDto
        {
            AvatarUrl = urlBuilder.BuildUrl(avatarPath)!
        });
    }

    public Task<Result<OnlineUsersResponseDto>> GetOnlineUsersAsync(CancellationToken ct = default)
    {
        var ids = onlineService.GetOnlineUserIds().ToList();
        return Task.FromResult(Result<OnlineUsersResponseDto>.Success(
            new OnlineUsersResponseDto
            {
                OnlineUserIds = ids,
                TotalOnline = ids.Count
            }));
    }

    public async Task<Result<OnlineStatusDto>> GetOnlineStatusAsync(int userId, CancellationToken ct = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.LastOnline })
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return Result<OnlineStatusDto>.Success(
            new OnlineStatusDto(userId, onlineService.IsOnline(userId), user?.LastOnline));
    }

    public async Task<Result<List<OnlineStatusDto>>> GetOnlineStatusesAsync(List<int> userIds, CancellationToken ct = default)
    {
        if (userIds is null || userIds.Count == 0)
            return Result<List<OnlineStatusDto>>.Failure("Список ID пользователей не может быть пустым");

        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .AsNoTracking()
            .Select(u => new { u.Id, u.LastOnline })
            .ToListAsync(ct);

        var onlineIds = onlineService.FilterOnline(userIds);

        var result = users.ConvertAll(u =>
            new OnlineStatusDto(u.Id, onlineIds.Contains(u.Id), u.LastOnline));

        return Result<List<OnlineStatusDto>>.Success(result);
    }

    public async Task<Result> ChangeUsernameAsync(int id, ChangeUsernameDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.NewUsername))
            return Result.Failure("Username не может быть пустым");

        var username = dto.NewUsername.Trim().ToLower();

        if (!UsernameRegex().IsMatch(username))
            return Result.Failure("Username должен содержать 3-30 символов (латинские буквы, цифры, подчёркивания)");

        var exists = await _context.Users
            .AnyAsync(u => u.Username == username && u.Id != id, ct);

        if (exists)
            return Result.Failure("Этот username уже занят");

        var user = await _context.Users.FindAsync([id], ct);
        if (user == null)
            return Result.Failure($"Пользователь с ID {id} не найден");

        user.Username = username;
        await SaveChangesAsync(ct);

        _logger.LogInformation("Username изменён для пользователя {UserId}", id);
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

        var user = await _context.Users.FindAsync([id], ct);
        if (user == null)
            return Result.Failure($"Пользователь с ID {id} не найден");

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return Result.Failure("Неверный текущий пароль");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await SaveChangesAsync(ct);

        _logger.LogInformation("Пароль изменён для пользователя {UserId}", id);
        return Result.Success();
    }
}