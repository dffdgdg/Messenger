using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Chat;

namespace API.Application.Features.Notification.Commands;

public class SetChatMuteCommandHandler(IUnitOfWork unitOfWork, IChatRepository chatRepository)
    : ICommandHandler<SetChatMuteCommand, ChatNotificationSettingsDto>
{
    public virtual async Task<Result<ChatNotificationSettingsDto>> HandleAsync(SetChatMuteCommand command, CancellationToken ct = default)
    {
        var member = await chatRepository.GetMemberAsync(command.Request.ChatId, command.UserId, ct);

        if (member is null)
            return Result<ChatNotificationSettingsDto>.Failure($"Пользователь не является участником чата {command.Request.ChatId}");

        member.NotificationsEnabled = command.Request.NotificationsEnabled;

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<ChatNotificationSettingsDto>();

        return Result<ChatNotificationSettingsDto>.Success(new ChatNotificationSettingsDto
        {
            ChatId = command.Request.ChatId,
            NotificationsEnabled = member.NotificationsEnabled
        });
    }
}

