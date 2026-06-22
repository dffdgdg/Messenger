using API.Application.Bundles;
using API.Application.Common;
using API.Application.Configuration;
using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Dto.User;
using Shared.Enum;
using Shared.Hubs;
using System.Collections.Concurrent;

namespace API.Application.Services.Features.User;

public partial class AdminService(
    IUnitOfWork unitOfWork,
    IDepartmentRepository departmentRepository,
    TimeBundle time,
    IUserRepository userRepo,
    IRefreshTokenRepository tokenRepo,
    ILogger<AdminService> logger,
    IHubNotifier hubNotifier,
    IOptions<MessengerSettings> messengerSettings,
    IDepartmentService departmentService) : BaseService<AdminService>(unitOfWork, logger), IAdminService
{
    private readonly AppDateTime appDateTime = time.AppDateTime;
    private readonly MessengerSettings _messengerSettings = messengerSettings.Value;
    private readonly IHubNotifier _hubNotifier = hubNotifier;
    private static readonly ConcurrentDictionary<int, DateTime> _lastNotificationTime = new();

    public async Task<Result<List<UserDto>>> GetUsersAsync(CancellationToken ct = default)
    {
        var users = await userRepo.GetAllWithSettingsAsync(ct);

        var result = users.ConvertAll(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            DisplayName = string.IsNullOrWhiteSpace(u.Surname)
                ? u.Username
                : $"{u.Surname} {u.Name} {u.Midname}".Trim(),
            Surname = u.Surname,
            Name = u.Name,
            Midname = u.Midname,
            Avatar = u.Avatar,
            DepartmentId = u.DepartmentId,
            Department = u.DepartmentName,
            IsBanned = u.IsBanned,
            LastOnline = u.LastOnline,
            Theme = u.Theme,
            NotificationsEnabled = u.NotificationsEnabled
        });

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

        var username = dto.Username!.Trim().ToLowerInvariant();

        if (await userRepo.UsernameExistsAsync(username, ct))
            return Result<UserDto>.Conflict("Пользователь с таким логином уже существует");

        if (dto.DepartmentId.HasValue)
        {
            var deptExists = await departmentRepository.ExistsAsync(dto.DepartmentId.Value, ct);
            if (!deptExists)
                return Result<UserDto>.NotFound("Указанный отдел не существует");
        }

        var user = new API.Domain.Entities.User
        {
            Username = username,
            Password = UserPassword.Create(dto.Password!),
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

        userRepo.Add(user);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<UserDto>();

        if (user.DepartmentId.HasValue)
        {
            var sync = await departmentService.SyncDepartmentChatMembershipAsync(user.Id, null, user.DepartmentId, ct);
            if (sync.IsFailure) return sync.As<UserDto>();
        }

        LogUserCreated(username, user.Id);

        var created = await userRepo.GetWithSettingsAsync(user.Id, ct);
        var result = new UserDto
        {
            Id = created!.Id,
            Username = created.Username,
            DisplayName = (created.Surname + " " + created.Name + (created.Midname != null ? " " + created.Midname : "")).Trim(),
            Surname = created.Surname,
            Name = created.Name,
            Midname = created.Midname,
            Avatar = created.Avatar,
            DepartmentId = created.DepartmentId,
            Department = created.DepartmentName,
            IsBanned = created.IsBanned,
            LastOnline = created.LastOnline,
            Theme = created.Theme,
            NotificationsEnabled = created.NotificationsEnabled
        };

        return Result<UserDto>.Success(result);
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

        var user = await userRepo.FindByIdTrackedAsync(userId, ct);
        if (user is null)
            return Result<UserDto>.NotFound($"Пользователь с ID {userId} не найден");

        var taken = await userRepo.UsernameExistsByOtherUserAsync(username, userId, ct);
        if (taken)
            return Result<UserDto>.Conflict("Пользователь с таким логином уже существует");

        if (dto.DepartmentId.HasValue)
        {
            var deptExists = await departmentRepository.ExistsAsync(dto.DepartmentId.Value, ct);
            if (!deptExists)
                return Result<UserDto>.NotFound("Указанный отдел не существует");
        }

        var previousDepartmentId = user.DepartmentId;
        var wasHead = await departmentRepository.IsHeadOfAnyDepartmentAsync(userId, ct);

        user.Username = username;
        user.Surname = dto.Surname.Trim();
        user.Name = dto.Name.Trim();
        user.Midname = dto.Midname?.Trim();
        user.DepartmentId = dto.DepartmentId;

        var departmentChatSync = await departmentService.SyncDepartmentChatMembershipAsync(userId, previousDepartmentId, dto.DepartmentId, ct);
        if (departmentChatSync.IsFailure) return departmentChatSync.As<UserDto>();

        var newRole = await DetermineUserRoleAsync(userId, ct);
        var oldRole = DetermineOldRole(previousDepartmentId, wasHead);

        if (newRole != oldRole)
        {
            await SendToUserOnceAsync(userId, HubMethods.Chat.UserRoleUpdated, newRole, ct);
            await SendToUserOnceAsync(userId, HubMethods.Chat.UserPermissionsChanged, new UserPermissionsChangedDto
            {
                UserId = userId,
                Role = newRole,
                Reason = newRole > oldRole
                    ? "Ваши права были повышены администратором"
                    : "Ваши права были изменены администратором"
            }, ct);
        }

        LogUserUpdated(userId);

        var updated = await userRepo.GetWithSettingsAsync(userId, ct);
        var resultDto = new UserDto
        {
            Id = updated!.Id,
            Username = updated.Username,
            DisplayName = (updated.Surname + " " + updated.Name + (updated.Midname != null ? " " + updated.Midname : "")).Trim(),
            Surname = updated.Surname,
            Name = updated.Name,
            Midname = updated.Midname,
            Avatar = updated.Avatar,
            DepartmentId = updated.DepartmentId,
            Department = updated.DepartmentName,
            IsBanned = updated.IsBanned,
            LastOnline = updated.LastOnline,
            Theme = updated.Theme,
            NotificationsEnabled = updated.NotificationsEnabled
        };

        return Result<UserDto>.Success(resultDto);
    }

    public async Task<Result> ToggleBanAsync(int userId, CancellationToken ct = default)
    {
        var user = await userRepo.FindByIdTrackedAsync(userId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {userId} не найден");

        user.IsBanned = !user.IsBanned;

        if (user.IsBanned)
        {
            await tokenRepo.RevokeAllForUserAsync(userId, appDateTime.UtcNow, ct);

            await SendToUserOnceAsync(userId, "UserBanned", new UserBannedDto
            {
                Reason = "Вы были забанены администратором"
            }, ct);
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

        var user = await userRepo.FindByIdWithPasswordAsync(userId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {userId} не найден");

        user.Password.SetPassword(newPassword);

        await tokenRepo.RevokeAllForUserAsync(userId, appDateTime.UtcNow, ct);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogPasswordReset(userId);
        return Result.Success();
    }

    private async Task SendToUserOnceAsync(int userId, string method, object dto, CancellationToken ct)
    {
        var now = appDateTime.UtcNow;
        if (_lastNotificationTime.TryGetValue(userId, out var last) && (now - last).TotalSeconds < 3)
            return;

        _lastNotificationTime[userId] = now;

        try
        {
            await _hubNotifier.SendToUserConnectionAsync(userId.ToString(), method, dto);
        }
        catch (Exception ex)
        {
            LogNotificationFailed(userId, method, ex.Message);
        }
    }

    private async Task<UserRole> DetermineUserRoleAsync(int userId, CancellationToken ct)
        => await departmentRepository.ResolveUserRoleAsync(userId, _messengerSettings.AdminDepartmentId, ct);

    private UserRole DetermineOldRole(int? previousDepartmentId, bool wasHead)
    {
        if (previousDepartmentId == _messengerSettings.AdminDepartmentId)
            return UserRole.Admin;
        if (wasHead)
            return UserRole.Head;
        return UserRole.User;
    }

    #region Log
    [LoggerMessage(Level = LogLevel.Information, Message = "Создан пользователь {Username} (ID={UserId})")]
    private partial void LogUserCreated(string username, int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Администратор обновил профиль пользователя ID={UserId}")]
    private partial void LogUserUpdated(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Администратор сбросил пароль пользователя ID={UserId}")]
    private partial void LogPasswordReset(int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь ID={UserId} {Action}", EventName = "UserBanStatusChanged")]
    private partial void LogBanStatusChanged(int userId, string action);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось отправить уведомление {Method} пользователю ID={UserId}: {Error}")]
    private partial void LogNotificationFailed(int userId, string method, string error);
    #endregion
}