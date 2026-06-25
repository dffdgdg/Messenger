using API.Application.Common;
using API.Application.Configuration;
using API.Application.Features.Department;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Projections;
using API.Domain.Repositories;
using Microsoft.Extensions.Options;
using Shared.Contracts.User;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Admin.Commands;

public class UpdateUserCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository, IDepartmentRepository departmentRepository,
    ICacheService cacheService, IHubNotifier hubNotifier, DepartmentSyncService departmentSync, IOptions<MessengerSettings> options)
    : ICommandHandler<UpdateUserCommand, UserDto>
{
    private readonly MessengerSettings settings = options.Value;
    public virtual async Task<Result<UserDto>> HandleAsync(UpdateUserCommand command, CancellationToken ct = default)
    {
        var (userId, dto) = (command.UserId, command.Dto);

        if (userId != dto.Id)
            return Result<UserDto>.Failure("Несоответствие идентификатора");

        var validation = ValidateDto(dto);
        if (validation.IsFailure) return validation.As<UserDto>();

        var username = dto.Username!.Trim().ToLowerInvariant();

        var user = await userRepository.FindByIdTrackedAsync(userId, ct);
        if (user is null)
            return Result<UserDto>.NotFound($"Пользователь с ID {userId} не найден");

        if (await userRepository.UsernameExistsByOtherUserAsync(username, userId, ct))
            return Result<UserDto>.Conflict("Пользователь с таким логином уже существует");

        if (dto.DepartmentId.HasValue)
        {
            var deptExists = await departmentRepository.ExistsAsync(dto.DepartmentId.Value, ct);
            if (!deptExists)
                return Result<UserDto>.NotFound("Указанный отдел не существует");
        }

        var previousDeptId = user.DepartmentId;
        var wasHead = await departmentRepository.IsHeadOfAnyDepartmentAsync(userId, ct);

        user.Username = username;
        user.Surname = dto.Surname!.Trim();
        user.Name = dto.Name!.Trim();
        user.Midname = dto.Midname?.Trim();
        user.DepartmentId = dto.DepartmentId;

        // Синхронизация чата отдела
        var syncResult = await departmentSync.SyncDepartmentChatMembershipAsync(userId, previousDeptId, dto.DepartmentId, ct);

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<UserDto>();

        await departmentSync.NotifyMembershipChangedAsync(userId, syncResult, cacheService, ct);

        // Уведомление об изменении роли
        await NotifyRoleChangedIfNeededAsync(userId, previousDeptId, wasHead, ct);

        var updated = await userRepository.GetWithSettingsAsync(userId, ct);

        return Result<UserDto>.Success(MapToDto(updated!));
    }

    private async Task NotifyRoleChangedIfNeededAsync(int userId, int? previousDeptId, bool wasHead, CancellationToken ct)
    {
        var newRole = await departmentRepository
            .ResolveUserRoleAsync(userId, settings.AdminDepartmentId, ct);

        var oldRole = DetermineOldRole(previousDeptId, wasHead);

        if (newRole == oldRole) return;

        await hubNotifier.SendToUserConnectionAsync(userId.ToString(), HubMethods.Chat.UserRoleUpdated, newRole);

        await hubNotifier.SendToUserConnectionAsync(userId.ToString(), HubMethods.Chat.UserPermissionsChanged, new UserPermissionsChangedDto
        {
            UserId = userId,
            Role = newRole,
            Reason = newRole > oldRole
                ? "Ваши права были повышены администратором"
                : "Ваши права были изменены администратором"
        });
    }

    private UserRole DetermineOldRole(int? previousDeptId, bool wasHead)
    {
        if (previousDeptId == settings.AdminDepartmentId) return UserRole.Admin;
        if (wasHead) return UserRole.Head;
        return UserRole.User;
    }

    private static Result ValidateDto(UserDto dto)
    {
        var usernameValidation = ValidationHelper.ValidateUsername(dto.Username);
        if (usernameValidation.IsFailure) return usernameValidation;

        if (string.IsNullOrWhiteSpace(dto.Surname))
            return Result.Failure("Фамилия не может быть пустой");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure("Имя не может быть пустым");

        return Result.Success();
    }

    private static UserDto MapToDto(UserWithSettingsProjection u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        DisplayName = $"{u.Surname} {u.Name} {u.Midname}".Trim(),
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
    };
}

