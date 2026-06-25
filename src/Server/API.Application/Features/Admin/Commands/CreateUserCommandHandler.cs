using API.Application.Common;
using API.Application.Features.Department;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Projections;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.Admin.Commands;

public class CreateUserCommandHandler(
    IUnitOfWork unitOfWork,
    IUserRepository userRepository,
    IDepartmentRepository departmentRepository,
    ICacheService cacheService,
    DepartmentSyncService departmentSync,
    AppDateTime appDateTime)
    : ICommandHandler<CreateUserCommand, UserDto>
{
    public virtual async Task<Result<UserDto>> HandleAsync(CreateUserCommand command, CancellationToken ct = default)
    {
        var dto = command.Dto;

        var validation = ValidateDto(dto);
        if (validation.IsFailure) return validation.As<UserDto>();

        var username = dto.Username!.Trim().ToLowerInvariant();

        if (await userRepository.UsernameExistsAsync(username, ct))
            return Result<UserDto>.Conflict("Пользователь с таким логином уже существует");

        if (dto.DepartmentId.HasValue)
        {
            var deptExists = await departmentRepository.ExistsAsync(dto.DepartmentId.Value, ct);
            if (!deptExists)
                return Result<UserDto>.NotFound("Указанный отдел не существует");
        }

        var user = new Domain.Entities.User
        {
            Username = username,
            Password = UserPassword.Create(dto.Password!),
            Surname = dto.Surname!.Trim(),
            Name = dto.Name!.Trim(),
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

        userRepository.Add(user);

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<UserDto>();

        // Синхронизация чата отдела — через DepartmentSyncService, не через IDepartmentService
        if (user.DepartmentId.HasValue)
        {
            var syncResult = await departmentSync.SyncDepartmentChatMembershipAsync(user.Id, null, user.DepartmentId, ct);

            var syncSave = await unitOfWork.SaveChangesAsync(ct);
            if (syncSave.IsFailure) return syncSave.As<UserDto>();

            await departmentSync.NotifyMembershipChangedAsync(user.Id, syncResult, cacheService, ct);
        }

        var created = await userRepository.GetWithSettingsAsync(user.Id, ct);

        return Result<UserDto>.Success(MapToDto(created!));
    }

    private static Result ValidateDto(CreateUserDto dto)
    {
        var usernameValidation = ValidationHelper.ValidateUsername(dto.Username);
        if (usernameValidation.IsFailure) return usernameValidation;

        var passwordValidation = ValidationHelper.ValidatePassword(dto.Password);
        if (passwordValidation.IsFailure) return passwordValidation;

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

