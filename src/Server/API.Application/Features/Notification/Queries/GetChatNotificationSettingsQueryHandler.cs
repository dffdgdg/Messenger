using API.Application.Common;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Chat;

namespace API.Application.Features.Notification.Queries;

public class GetChatNotificationSettingsQueryHandler(IChatRepository chatRepository)
    : IQueryHandler<GetChatNotificationSettingsQuery, Result<ChatNotificationSettingsDto>>
{
    public virtual async Task<Result<ChatNotificationSettingsDto>> HandleAsync(GetChatNotificationSettingsQuery query, CancellationToken ct = default)
    {
        var member = await chatRepository.GetMemberAsync(query.ChatId, query.UserId, ct);
        if (member is null)
            return Result<ChatNotificationSettingsDto>.Failure($"Пользователь не является участником чата {query.ChatId}");

        return Result<ChatNotificationSettingsDto>.Success(new ChatNotificationSettingsDto
        {
            ChatId = query.ChatId,
            NotificationsEnabled = member.NotificationsEnabled
        });
    }
}

