using API.Application.Common;
using API.Application.Features.Department.Queries;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;

namespace API.Application.Features.Department.Commands;

public class AddUserToDepartmentCommandHandler(
    IUnitOfWork unitOfWork,
    IDepartmentRepository departmentRepository,
    IUserRepository userRepository,
    ICacheService cacheService,
    DepartmentSyncService sync,
    CanManageDepartmentQueryHandler canManageHandler)
    : ICommandHandler<AddUserToDepartmentCommand>
{
    public virtual async Task<Result> HandleAsync(AddUserToDepartmentCommand command, CancellationToken ct = default)
    {
        var canManage = await canManageHandler.CanManageAsync(
            command.RequesterId, command.DepartmentId, ct);
        if (!canManage)
            return Result.Forbidden("Нет прав на управление отделом");

        if (!await departmentRepository.ExistsAsync(command.DepartmentId, ct))
            return Result.NotFound($"Отдел с ID {command.DepartmentId} не найден");

        var user = await userRepository.FindByIdTrackedAsync(command.UserId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {command.UserId} не найден");

        if (user.DepartmentId == command.DepartmentId)
            return Result.Conflict("Пользователь уже в этом отделе");

        if (user.DepartmentId.HasValue && !await canManageHandler.IsAdminAsync(command.RequesterId, ct))
            return Result.Forbidden("Только администратор может перемещать между отделами");

        var oldDeptId = user.DepartmentId;
        user.DepartmentId = command.DepartmentId;

        var syncResult = await sync.SyncDepartmentChatMembershipAsync(command.UserId, oldDeptId, command.DepartmentId, ct);

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        await sync.NotifyMembershipChangedAsync(command.UserId, syncResult, cacheService, ct);
        await sync.NotifyUserRoleAsync(command.UserId, ct);

        return Result.Success();
    }
}

