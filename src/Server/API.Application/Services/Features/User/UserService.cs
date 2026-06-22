using API.Application.Bundles;
using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using API.Domain.Projections;
using API.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shared.Dto.Online;
using Shared.Dto.User;
using Shared.Enum;
using System.Globalization;

namespace API.Application.Services.Features.User;

public partial class UserService(
    IUnitOfWork unitOfWork,
    MediaBundle media,
    PresenceBundle presence,
    UrlBundle url,
    IUserRepository userRepo,
    ILogger<UserService> logger) : BaseService<UserService>(unitOfWork, logger), IUserService
{
    private readonly IFileService _fileService = media.FileService;
    private readonly IOnlineUserService _onlineService = presence.OnlineService;
    private readonly IUrlBuilder _urlBuilder = url.UrlBuilder;
    private readonly IUserRepository _userRepo = userRepo;

    public async Task<Result<List<UserDto>>> GetAllUsersAsync(CancellationToken ct = default)
    {
        var users = await _userRepo.GetAllWithSettingsAsync(ct);
        var onlineIds = _onlineService.GetOnlineUserIds();

        var result = users.ConvertAll(u => MapProjectionToDto(u, onlineIds.Contains(u.Id)));
        return Result<List<UserDto>>.Success(result);
    }

    public async Task<Result<UserDto>> GetUserAsync(int id, CancellationToken ct = default)
    {
        var user = await _userRepo.GetWithSettingsAsync(id, ct);
        if (user is null)
            return Result<UserDto>.NotFound($"Пользователь с ID {id} не найден");

        var dto = MapProjectionToDto(user, _onlineService.IsOnline(id));
        return Result<UserDto>.Success(dto);
    }

    public async Task<Result> UpdateUserAsync(int id, UserDto dto, CancellationToken ct = default)
    {
        if (id != dto.Id)
            return Result.Failure("Несоответствие ID");

        var user = await _userRepo.FindByIdWithSettingsTrackedAsync(id, ct);

        if (user is null)
            return Result.NotFound($"Пользователь с ID {id} не найден");

        user.UpdateProfile(dto);

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure) return saveResult;

        LogUserUpdated(id);
        return Result.Success();
    }

    public async Task<Result<AvatarResponseDto>> UploadAvatarAsync(int id, IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return Result<AvatarResponseDto>.Failure("Файл не предоставлен");

        var user = await _userRepo.FindByIdAsync(id, ct);
        if (user is null)
            return Result<AvatarResponseDto>.NotFound($"Пользователь с ID {id} не найден");

        var saveResult = await _fileService.SaveImageAsync(file, "avatars/users", user.Avatar);
        if (saveResult.IsFailure) return saveResult.As<AvatarResponseDto>();

        user.Avatar = saveResult.Value;

        var dbSave = await SaveChangesAsync(ct);
        if (dbSave.IsFailure) return dbSave.As<AvatarResponseDto>();

        LogAvatarUpdated(id);

        return Result<AvatarResponseDto>.Success(new AvatarResponseDto
        {
            AvatarUrl = _urlBuilder.BuildUrl(saveResult.Value)!
        });
    }

    public async Task<Result> RemoveAvatarAsync(int id, CancellationToken ct = default)
    {
        var user = await _userRepo.FindByIdAsync(id, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {id} не найден");

        if (!string.IsNullOrWhiteSpace(user.Avatar))
            _fileService.DeleteFile(user.Avatar);

        user.Avatar = null;

        var dbSave = await SaveChangesAsync(ct);
        if (dbSave.IsFailure) return dbSave;

        LogAvatarRemoved(id);
        return Result.Success();
    }

    public Task<Result<OnlineUsersResponseDto>> GetOnlineUsersAsync(CancellationToken ct = default)
    {
        var onlineIds = _onlineService.GetOnlineUserIds();
        return Task.FromResult(Result<OnlineUsersResponseDto>.Success(
            new OnlineUsersResponseDto
            {
                OnlineUserIds = [.. onlineIds],
                TotalOnline = onlineIds.Count
            }));
    }

    public async Task<Result<UserStatusDto>> GetOnlineStatusAsync(int userId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetWithSettingsAsync(userId, ct);

        var isOnline = _onlineService.IsOnline(userId);

        return Result<UserStatusDto>.Success(new UserStatusDto(
            userId,
            isOnline,
            user?.LastOnline,
            isOnline ? user?.StatusType ?? UserStatusType.Online : UserStatusType.Online,
            user?.StatusExpiresAt));
    }

    public async Task<Result<List<UserStatusDto>>> GetOnlineStatusesAsync(List<int> userIds, CancellationToken ct = default)
    {
        if (userIds is null || userIds.Count == 0)
            return Result<List<UserStatusDto>>.Failure("Список ID пользователей не может быть пустым");

        var users = await _userRepo.GetByIdsAsync(userIds, ct);

        var onlineIds = _onlineService.FilterOnline(userIds);
        var result = users.ConvertAll(u =>
            new UserStatusDto(u.Id, onlineIds.Contains(u.Id), u.LastOnline));

        return Result<List<UserStatusDto>>.Success(result);
    }

    public async Task<Result> ChangeUsernameAsync(
        int id, ChangeUsernameDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.NewUsername))
            return Result.Failure("Username не может быть пустым");

        var username = dto.NewUsername.Trim().ToLower(new CultureInfo("en-US", false));

        var validation = ValidationHelper.ValidateUsername(username);
        if (validation.IsFailure) return validation;

        var taken = await _userRepo.UsernameExistsByOtherUserAsync(username, id, ct);
        if (taken)
            return Result.Conflict("Этот username уже занят");

        var user = await _userRepo.FindByIdTrackedAsync(id, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {id} не найден");

        user.Username = username;

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogUsernameChanged(id);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(
        int id, ChangePasswordDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
            return Result.Failure("Введите текущий пароль");

        if (string.IsNullOrWhiteSpace(dto.NewPassword))
            return Result.Failure("Введите новый пароль");

        if (dto.NewPassword.Length < 6)
            return Result.Failure("Пароль должен содержать минимум 6 символов");

        var user = await _userRepo.FindByIdWithPasswordAsync(id, ct);
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

    private UserDto MapProjectionToDto(UserWithSettingsProjection u, bool isOnline)
        => new()
        {
            Id = u.Id,
            Username = u.Username,
            DisplayName = FormatDisplayName(u.Surname, u.Name, u.Midname),
            Surname = u.Surname,
            Name = u.Name,
            Midname = u.Midname,
            Avatar = _urlBuilder.BuildUrl(u.Avatar),
            DepartmentId = u.DepartmentId,
            Department = u.DepartmentName,
            IsBanned = u.IsBanned,
            LastOnline = u.LastOnline,
            Theme = u.Theme,
            NotificationsEnabled = u.NotificationsEnabled,
            IsOnline = isOnline,
            StatusType = isOnline ? u.StatusType : UserStatusType.Online,
            StatusExpiresAt = u.StatusExpiresAt
        };

    private static string? FormatDisplayName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : null;
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} обновлён")]
    private partial void LogUserUpdated(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Аватар обновлён для пользователя {UserId}")]
    private partial void LogAvatarUpdated(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Аватар удалён для пользователя {UserId}")]
    private partial void LogAvatarRemoved(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Username изменён для пользователя {UserId}")]
    private partial void LogUsernameChanged(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пароль изменён для пользователя {UserId}")]
    private partial void LogPasswordChanged(int userId);

    #endregion
}