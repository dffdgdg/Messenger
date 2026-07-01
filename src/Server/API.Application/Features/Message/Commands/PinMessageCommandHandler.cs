using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Message;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Message.Commands;

public class PinMessageCommandHandler(
    IMessageRepository messageRepository,
    IAccessControlService accessControl,
    ISystemMessageService systemMessages,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder,
    AppDateTime appDateTime)
    : ICommandHandler<PinMessageCommand, MessageDto>
{
    public virtual async Task<Result<MessageDto>> HandleAsync(PinMessageCommand command, CancellationToken ct = default)
    {
        var message = await messageRepository.FindUserMessageByIdAsync(command.MessageId, ct);
        if (message is null)
            return Result<MessageDto>.NotFound($"Сообщение {command.MessageId} не найдено");

        var access = await accessControl.EnsureMemberOfAsync(command.UserId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (message.IsDeleted == true)
            return Result<MessageDto>.Failure("Нельзя закрепить удалённое сообщение");

        if (message.PinnedAt != null)
            return Result<MessageDto>.Failure("Сообщение уже закреплено");

        await messageRepository.PinAsync(command.MessageId, command.UserId, appDateTime.UtcNow, ct);

        var updated = await messageRepository.FindUserMessageWithIncludesNoTrackingAsync(command.MessageId, ct);
        if (updated is null)
            return Result<MessageDto>.Internal("Не удалось загрузить сообщение после закрепления");

        var dto = updated.ToDto(command.UserId, urlBuilder);
        await hubNotifier.SendToChatAsync(updated.ChatId, HubMethods.Chat.MessageUpdated, dto);

        var preView = updated.Content?.Length > 50 ? updated.Content[..50] + "..." : updated.Content;

        await systemMessages.CreateAsync(updated.ChatId, command.UserId, SystemEventType.MessagePinned, content: preView);

        return Result<MessageDto>.Success(dto);
    }
}

