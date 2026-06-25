using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;

namespace API.Application.Features.User.Commands;

public class RemoveUserAvatarCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository, IFileService fileService)
    : ICommandHandler<RemoveUserAvatarCommand>
{
    public virtual async Task<Result> HandleAsync(RemoveUserAvatarCommand command, CancellationToken ct = default)
    {
        var user = await userRepository.FindByIdAsync(command.UserId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {command.UserId} не найден");

        if (!string.IsNullOrWhiteSpace(user.Avatar))
            fileService.DeleteFile(user.Avatar);

        user.Avatar = null;

        return await unitOfWork.SaveChangesAsync(ct);
    }
}

