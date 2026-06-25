using API.Application.Common;
using API.Application.Features.Department.Queries;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;

namespace API.Application.Features.Department.Commands;

public class RemoveUserFromDepartmentCommandHandler(
    IUnitOfWork unitOfWork,
    IDepartmentRepository departmentRepository,
    IUserRepository userRepository,
    ICacheService cacheService,
    DepartmentSyncService sync,
    CanManageDepartmentQueryHandler canManageHandler)
    : ICommandHandler<RemoveUserFromDepartmentCommand>
{
    public virtual async Task<Result> HandleAsync(RemoveUserFromDepartmentCommand command, CancellationToken ct = default)
    {
        var canManage = await canManageHandler.CanManageAsync(command.RequesterId, command.DepartmentId, ct);
        if (!canManage)
            return Result.Forbidden("Нет прав на управление отделом");

        var user = await userRepository.FindByIdTrackedAsync(command.UserId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {command.UserId} не найден");

        if (user.DepartmentId != command.DepartmentId)
            return Result.Failure("Пользователь не в этом отделе");

        var department = await departmentRepository.FindByIdAsync(command.DepartmentId, ct);
        if (department?.HeadId == command.UserId)
            return Result.Failure("Сначала назначьте другого начальника");

        var oldDeptId = user.DepartmentId;
        user.DepartmentId = null;

        var syncResult = await sync.SyncDepartmentChatMembershipAsync(command.UserId, oldDeptId, null, ct);

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        await sync.NotifyMembershipChangedAsync(command.UserId, syncResult, cacheService, ct);
        await sync.NotifyUserRoleAsync(command.UserId, ct);

        return Result.Success();
    }
}

