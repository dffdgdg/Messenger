using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Message;
using Shared.HubProtocol;

namespace API.Application.Features.Message.Commands;

public class UpdateMessageCommandHandler(
    IUnitOfWork unitOfWork,
    IMessageRepository messageRepository,
    IAccessControlService accessControl,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder,
    AppDateTime appDateTime)
    : ICommandHandler<UpdateMessageCommand, MessageDto>
{
    public virtual async Task<Result<MessageDto>> HandleAsync(UpdateMessageCommand command, CancellationToken ct = default)
    {
        var message = await messageRepository.FindUserMessageWithIncludesAsync(command.MessageId, ct);
        if (message is null)
            return Result<MessageDto>.NotFound($"Сообщение {command.MessageId} не найдено");

        var access = await accessControl.EnsureMemberOfAsync(command.UserId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (message.SenderId != command.UserId)
            return Result<MessageDto>.Forbidden("Вы можете изменять только свои сообщения");

        var error = message switch
        {
            { IsDeleted: true } => "Сообщение уже удалено",
            _ when message.Poll != null => "Нельзя редактировать сообщение с опросом",
            { IsVoiceMessage: true } => "Нельзя редактировать голосовое сообщение",
            { ForwardedFromMessageId: not null } => "Нельзя редактировать пересланное сообщение",
            _ when string.IsNullOrWhiteSpace(
                command.Dto.Content) => "Содержимое сообщения не может быть пустым",
            _ => null
        };

        if (error is not null)
            return Result<MessageDto>.Failure(error);

        message.Content = command.Dto.Content!.Trim();
        message.EditedAt = appDateTime.UtcNow;

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<MessageDto>();

        var dto = message.ToDto(command.UserId, urlBuilder);
        await hubNotifier.SendToChatAsync(message.ChatId, HubMethods.Chat.MessageUpdated, dto);

        return Result<MessageDto>.Success(dto);
    }
}

