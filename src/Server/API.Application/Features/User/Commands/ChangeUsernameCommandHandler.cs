using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.User;
using System.Globalization;

namespace API.Application.Features.User.Commands;

public class ChangeUsernameCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository)
    : ICommandHandler<ChangeUsernameCommand>
{
    public virtual async Task<Result> HandleAsync(ChangeUsernameCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Dto.NewUsername))
            return Result.Failure("Username не может быть пустым");

        var username = command.Dto.NewUsername.Trim().ToLower(new CultureInfo("en-US", false));

        var validation = ValidationHelper.ValidateUsername(username);
        if (validation.IsFailure) return validation;

        if (await userRepository.UsernameExistsByOtherUserAsync(username, command.UserId, ct))
            return Result.Conflict("Этот username уже занят");

        var user = await userRepository.FindByIdTrackedAsync(command.UserId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {command.UserId} не найден");

        user.Username = username;

        return await unitOfWork.SaveChangesAsync(ct);
    }
}

