using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.HubProtocol;

namespace API.Application.Features.Message.Commands;

public class DeleteMessageCommandHandler(
    IUnitOfWork unitOfWork,
    IMessageRepository messageRepository,
    IAccessControlService accessControl,
    IFileService fileService,
    IHubNotifier hubNotifier,
    AppDateTime appDateTime)
    : ICommandHandler<DeleteMessageCommand>
{
    public virtual async Task<Result> HandleAsync(DeleteMessageCommand command, CancellationToken ct = default)
    {
        var message = await messageRepository.FindUserMessageForDeleteAsync(command.MessageId, ct);
        if (message is null)
            return Result.NotFound($"Сообщение с ID {command.MessageId} не найдено");

        var access = await accessControl.EnsureMemberOfAsync(command.UserId, message.ChatId);
        if (access.IsFailure) return access;

        var canDelete = message.SenderId == command.UserId || await accessControl.IsAdminAsync(command.UserId, message.ChatId);
        if (!canDelete)
            return Result.Forbidden("Вы можете удалять только свои сообщения");

        if (message.IsDeleted == true)
            return Result.Failure("Сообщение уже удалено");

        // Голосовое — удаляем файл до soft delete
        if (message.VoiceMessage != null)
        {
            fileService.DeleteFile(message.VoiceMessage.FilePath);
            messageRepository.RemoveVoiceMessage(message.VoiceMessage);
            await unitOfWork.SaveChangesAsync(ct);
        }

        await messageRepository.SoftDeleteAsync(command.MessageId, appDateTime.UtcNow, ct);

        await hubNotifier.SendToChatAsync(message.ChatId, HubMethods.Chat.MessageDeleted,
            new { MessageId = command.MessageId, message.ChatId });

        return Result.Success();
    }
}

