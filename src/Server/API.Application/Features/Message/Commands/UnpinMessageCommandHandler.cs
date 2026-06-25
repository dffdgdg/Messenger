using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Message;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Message.Commands;

public class UnpinMessageCommandHandler(
    IMessageRepository messageRepository,
    IAccessControlService accessControl,
    ISystemMessageService systemMessages,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder)
    : ICommandHandler<UnpinMessageCommand, MessageDto>
{
    public virtual async Task<Result<MessageDto>> HandleAsync(UnpinMessageCommand command, CancellationToken ct = default)
    {
        var message = await messageRepository.FindUserMessageByIdAsync(command.MessageId, ct);
        if (message is null)
            return Result<MessageDto>.NotFound($"Сообщение {command.MessageId} не найдено");

        var access = await accessControl.EnsureMemberOfAsync(command.UserId, message.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (message.PinnedAt is null)
            return Result<MessageDto>.Failure("Сообщение уже не закреплено");

        await messageRepository.UnpinAsync(command.MessageId, ct);

        var updated = await messageRepository.FindUserMessageWithIncludesNoTrackingAsync(command.MessageId, ct);
        if (updated is null)
            return Result<MessageDto>.Internal("Не удалось загрузить сообщение после открепления");

        var dto = updated.ToDto(command.UserId, urlBuilder);
        await hubNotifier.SendToChatAsync(updated.ChatId, HubMethods.Chat.MessageUpdated, dto);

        await systemMessages.CreateAsync(updated.ChatId, command.UserId, SystemEventType.MessageUnpinned);

        return Result<MessageDto>.Success(dto);
    }
}

