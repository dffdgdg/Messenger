using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.User.Commands;

public class ChangePasswordCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository)
    : ICommandHandler<ChangePasswordCommand>
{
    public virtual async Task<Result> HandleAsync(ChangePasswordCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Dto.CurrentPassword))
            return Result.Failure("Введите текущий пароль");

        if (string.IsNullOrWhiteSpace(command.Dto.NewPassword))
            return Result.Failure("Введите новый пароль");

        if (command.Dto.NewPassword.Length < 6)
            return Result.Failure("Пароль должен содержать минимум 6 символов");

        var user = await userRepository.FindByIdWithPasswordAsync(command.UserId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {command.UserId} не найден");

        if (!user.Password.Verify(command.Dto.CurrentPassword))
            return Result.Unauthorized("Неверный текущий пароль");

        user.Password.SetPassword(command.Dto.NewPassword);

        return await unitOfWork.SaveChangesAsync(ct);
    }
}

