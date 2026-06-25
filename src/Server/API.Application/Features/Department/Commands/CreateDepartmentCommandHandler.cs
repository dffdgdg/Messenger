using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Shared.Contracts.Department;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Department.Commands;

public class CreateDepartmentCommandHandler(
    IUnitOfWork unitOfWork,
    IDepartmentRepository departmentRepository,
    IUserRepository userRepository,
    IChatRepository chatRepository,
    DepartmentSyncService sync,
    AppDateTime appDateTime)
    : ICommandHandler<CreateDepartmentCommand, DepartmentDto>
{
    public virtual async Task<Result<DepartmentDto>> HandleAsync(CreateDepartmentCommand command, CancellationToken ct = default)
    {
        var dto = command.Dto;

        var validation = await ValidateAsync(dto, ct);
        if (validation.IsFailure) return validation.As<DepartmentDto>();

        var entity = new Domain.Entities.Department
        {
            Name = dto.Name!.Trim(),
            ParentDepartmentId = dto.ParentDepartmentId,
            HeadId = dto.Head
        };

        var departmentChat = new Domain.Entities.Chat
        {
            Name = $"Отдел {entity.Name}",
            Type = ChatType.Department,
            CreatedById = 1,
            CreatedAt = appDateTime.UtcNow
        };

        chatRepository.Add(departmentChat);
        departmentRepository.Add(entity);

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<DepartmentDto>();

        entity.ChatId = departmentChat.Id;

        var linkSave = await unitOfWork.SaveChangesAsync(ct);
        if (linkSave.IsFailure) return linkSave.As<DepartmentDto>();

        if (dto.Head.HasValue)
        {
            var headUser = await userRepository.FindByIdTrackedAsync(dto.Head.Value, ct);
            if (headUser != null)
            {
                var oldDeptId = headUser.DepartmentId;
                headUser.DepartmentId = entity.Id;

                await sync.SyncDepartmentChatMembershipAsync(headUser.Id, oldDeptId, entity.Id, ct);

                await sync.NotifyUserRoleAsync(headUser.Id, ct);
            }
        }

        await sync.SyncHeadsChatAsync(null, entity.HeadId, ct);

        var headsSave = await unitOfWork.SaveChangesAsync(ct);
        if (headsSave.IsFailure) return headsSave.As<DepartmentDto>();

        dto.Id = entity.Id;
        dto.UserCount = 0;

        return Result<DepartmentDto>.Success(dto);
    }

    private async Task<Result> ValidateAsync(DepartmentDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure("Название обязательно");

        if (dto.ParentDepartmentId.HasValue)
        {
            var parentExists = await departmentRepository.ExistsAsync(dto.ParentDepartmentId.Value, ct);
            if (!parentExists)
                return Result.NotFound("Родительский отдел не существует");
        }

        if (dto.Head.HasValue)
        {
            var headExists = await userRepository.ExistsAsync(dto.Head.Value, ct);
            if (!headExists)
                return Result.NotFound("Указанный пользователь не существует");
        }

        return Result.Success();
    }
}

