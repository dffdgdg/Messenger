using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.User.Commands;

public class UpdateUserCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository)
    : ICommandHandler<UpdateUserCommand>
{
    public virtual async Task<Result> HandleAsync(UpdateUserCommand command, CancellationToken ct = default)
    {
        if (command.UserId != command.Dto.Id)
            return Result.Failure("Несоответствие ID");

        var user = await userRepository.FindByIdWithSettingsTrackedAsync(command.UserId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {command.UserId} не найден");

        user.UpdateProfile(command.Dto);

        return await unitOfWork.SaveChangesAsync(ct);
    }
}

