using API.Application.Common;
using API.Application.Configuration;
using API.Application.Features.Department.Queries;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Microsoft.Extensions.Options;
using Shared.Contracts.Department;
using Shared.Enum;

namespace API.Application.Features.Department.Commands;

public class UpdateDepartmentCommandHandler(
    IUnitOfWork unitOfWork,
    IDepartmentRepository departmentRepository,
    IUserRepository userRepository,
    IChatRepository chatRepository,
    ICacheService cacheService,
    DepartmentSyncService sync,
    CanManageDepartmentQueryHandler canManageHandler,
    IOptions<MessengerSettings> settings)
    : ICommandHandler<UpdateDepartmentCommand>
{
    private readonly MessengerSettings _settings = settings.Value;

    public virtual async Task<Result> HandleAsync(UpdateDepartmentCommand command, CancellationToken ct = default)
    {
        var entity = await departmentRepository.FindByIdAsync(command.DepartmentId, ct);
        if (entity is null)
            return Result.NotFound($"Отдел с ID {command.DepartmentId} не найден");

        var validation = await ValidateAsync(command, entity, ct);
        if (validation.IsFailure) return validation;

        var oldHeadId = entity.HeadId;

        // Если руководитель уже ведёт другой отдел — снимаем его оттуда
        if (command.Dto.Head.HasValue && command.Dto.Head != oldHeadId)
        {
            var relocate = await RelocateHeadIfNeededAsync(command.Dto.Head.Value, command.DepartmentId, ct);
            if (relocate.IsFailure) return relocate;
        }

        entity.Name = command.Dto.Name!.Trim();
        entity.ParentDepartmentId = command.Dto.ParentDepartmentId;
        entity.HeadId = command.Dto.Head;

        // Переносим нового руководителя в отдел если нужно
        if (command.Dto.Head.HasValue && command.Dto.Head != oldHeadId)
        {
            var headUser = await userRepository.FindByIdTrackedAsync(command.Dto.Head.Value, ct);
            if (headUser != null && headUser.DepartmentId != command.DepartmentId)
            {
                var oldUserDeptId = headUser.DepartmentId;
                headUser.DepartmentId = command.DepartmentId;

                await sync.SyncDepartmentChatMembershipAsync(headUser.Id, oldUserDeptId, command.DepartmentId, ct);

                await sync.NotifyUserRoleAsync(headUser.Id, ct);
            }
        }

        // Переименовываем чат отдела если нужно
        if (entity.ChatId.HasValue && !string.Equals(entity.Name, command.Dto.Name!.Trim(), StringComparison.Ordinal))
        {
            var chat = await chatRepository.GetChatWithTrackingAsync(entity.ChatId.Value, ct);
            if (chat is not null)
                chat.Name = $"Отдел {entity.Name}";
        }

        await sync.SyncHeadsChatAsync(oldHeadId, entity.HeadId, ct, excludeDepartmentId: entity.Id);

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        // Уведомления об изменении роли
        if (oldHeadId.HasValue && oldHeadId != entity.HeadId)
            await sync.NotifyUserRoleAsync(oldHeadId.Value, ct);

        if (entity.HeadId.HasValue && entity.HeadId != oldHeadId)
            await sync.NotifyUserRoleAsync(entity.HeadId.Value, ct);

        return Result.Success();
    }

    private async Task<Result> ValidateAsync(UpdateDepartmentCommand command, Domain.Entities.Department entity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Dto.Name))
            return Result.Failure("Название обязательно");

        if (command.Dto.ParentDepartmentId == command.DepartmentId)
            return Result.Failure("Отдел не может быть родителем самому себе");

        if (command.Dto.ParentDepartmentId.HasValue)
        {
            var cycleResult = await CheckNoCycleAsync(command.DepartmentId, command.Dto.ParentDepartmentId.Value, ct);
            if (cycleResult.IsFailure) return cycleResult;
        }

        if (command.Dto.Head.HasValue)
        {
            var headExists = await userRepository.ExistsAsync(command.Dto.Head.Value, ct);
            if (!headExists)
                return Result.NotFound("Указанный пользователь не существует");
        }

        return Result.Success();
    }

    private async Task<Result> RelocateHeadIfNeededAsync(int newHeadId, int departmentId, CancellationToken ct)
    {
        var departments = await departmentRepository.GetAllAsync(ct);
        var otherDept = departments.FirstOrDefault(d => d.HeadId == newHeadId && d.Id != departmentId);

        if (otherDept is null) return Result.Success();

        var otherOldHeadId = otherDept.HeadId;
        otherDept.HeadId = null;

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        if (otherOldHeadId.HasValue)
        {
            await sync.NotifyUserRoleAsync(otherOldHeadId.Value, ct);
            await sync.SyncHeadsChatAsync(otherOldHeadId, null, ct, excludeDepartmentId: otherDept.Id);
        }

        return Result.Success();
    }

    private async Task<Result> CheckNoCycleAsync(int departmentId, int parentId, CancellationToken ct)
    {
        var all = await departmentRepository.GetAllAsync(ct);
        var visited = new HashSet<int> { departmentId };
        var queue = new Queue<int>();
        queue.Enqueue(parentId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!visited.Add(current))
                return Result.Failure("Нельзя установить дочерний отдел как родительский — обнаружен цикл");

            var parent = all.FirstOrDefault(d => d.Id == current);
            if (parent?.ParentDepartmentId.HasValue == true)
                queue.Enqueue(parent.ParentDepartmentId.Value);
        }

        return Result.Success();
    }
}

