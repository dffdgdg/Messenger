using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;

namespace API.Application.Features.Department.Commands;

public class DeleteDepartmentCommandHandler(IUnitOfWork unitOfWork, IDepartmentRepository departmentRepository,
    IChatRepository chatRepository, DepartmentSyncService sync)
    : ICommandHandler<DeleteDepartmentCommand>
{
    public virtual async Task<Result> HandleAsync(DeleteDepartmentCommand command, CancellationToken ct = default)
    {
        var entity = await departmentRepository.FindByIdAsync(command.DepartmentId, ct);
        if (entity is null)
            return Result.NotFound($"Отдел с ID {command.DepartmentId} не найден");

        if (await departmentRepository.HasChildDepartmentsAsync(command.DepartmentId, ct))
            return Result.Failure("Нельзя удалить отдел с дочерними отделами");

        if (await departmentRepository.HasUsersAsync(command.DepartmentId, ct))
            return Result.Failure("Нельзя удалить отдел с сотрудниками");

        await sync.SyncHeadsChatAsync(entity.HeadId, null, ct,
            excludeDepartmentId: entity.Id);

        if (entity.ChatId.HasValue)
        {
            var chat = await chatRepository
                .GetChatWithTrackingAsync(entity.ChatId.Value, ct);
            if (chat is not null)
                chatRepository.Remove(chat);
        }

        departmentRepository.Remove(entity);

        return await unitOfWork.SaveChangesAsync(ct);
    }
}

