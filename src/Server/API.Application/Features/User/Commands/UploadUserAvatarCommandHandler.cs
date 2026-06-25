using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.User.Commands;

public class UploadUserAvatarCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository,
    IFileService fileService, IUrlBuilder urlBuilder)
    : ICommandHandler<UploadUserAvatarCommand, AvatarResponseDto>
{
    public virtual async Task<Result<AvatarResponseDto>> HandleAsync(UploadUserAvatarCommand command, CancellationToken ct = default)
    {
        if (command.File is null || command.File.Length == 0)
            return Result<AvatarResponseDto>.Failure("Файл не предоставлен");

        var user = await userRepository.FindByIdAsync(command.UserId, ct);
        if (user is null)
            return Result<AvatarResponseDto>.NotFound($"Пользователь с ID {command.UserId} не найден");

        var saveResult = await fileService.SaveImageAsync(command.File, "avatars/users", user.Avatar);
        if (saveResult.IsFailure) return saveResult.As<AvatarResponseDto>();

        user.Avatar = saveResult.Value;

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<AvatarResponseDto>();

        return Result<AvatarResponseDto>.Success(new AvatarResponseDto
        {
            AvatarUrl = urlBuilder.BuildUrl(saveResult.Value)!
        });
    }
}

