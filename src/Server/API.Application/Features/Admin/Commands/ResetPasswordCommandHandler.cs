using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;

namespace API.Application.Features.Admin.Commands;

public class ResetPasswordCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository, IRefreshTokenRepository tokenRepository,
    AppDateTime appDateTime) : ICommandHandler<ResetPasswordCommand>
{
    public virtual async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken ct = default)
    {
        var passwordValidation = ValidationHelper.ValidatePassword(command.NewPassword);
        if (passwordValidation.IsFailure) return passwordValidation;

        var user = await userRepository.FindByIdWithPasswordAsync(command.UserId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {command.UserId} не найден");

        user.Password.SetPassword(command.NewPassword);

        await tokenRepository.RevokeAllForUserAsync(command.UserId, appDateTime.UtcNow, ct);

        return await unitOfWork.SaveChangesAsync(ct);
    }
}

