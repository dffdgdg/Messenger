using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.Admin.Commands;

public class ToggleBanCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository, IRefreshTokenRepository tokenRepository,
    IHubNotifier hubNotifier, AppDateTime appDateTime) : ICommandHandler<ToggleBanCommand>
{
    public virtual async Task<Result> HandleAsync(ToggleBanCommand command, CancellationToken ct = default)
    {
        var user = await userRepository.FindByIdTrackedAsync(command.UserId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {command.UserId} не найден");

        user.IsBanned = !user.IsBanned;

        if (user.IsBanned)
        {
            await tokenRepository.RevokeAllForUserAsync(command.UserId, appDateTime.UtcNow, ct);

            await hubNotifier.SendToUserConnectionAsync(command.UserId.ToString(), "UserBanned",
                new UserBannedDto { Reason = "Вы были забанены администратором" });
        }

        return await unitOfWork.SaveChangesAsync(ct);
    }
}

