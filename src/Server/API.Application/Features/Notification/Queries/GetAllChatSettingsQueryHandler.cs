using API.Application.Common;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Chat;

namespace API.Application.Features.Notification.Queries;

public class GetAllChatSettingsQueryHandler(IChatRepository chatRepository)
    : IQueryHandler<GetAllChatSettingsQuery, Result<List<ChatNotificationSettingsDto>>>
{
    public virtual async Task<Result<List<ChatNotificationSettingsDto>>> HandleAsync(GetAllChatSettingsQuery query, CancellationToken ct = default)
    {
        var chatIds = await chatRepository.GetMemberIdsForUserAsync(query.UserId, ct);
        if (chatIds.Count == 0)
            return Result<List<ChatNotificationSettingsDto>>.Success([]);

        var result = new List<ChatNotificationSettingsDto>(chatIds.Count);

        foreach (var chatId in chatIds)
        {
            var member = await chatRepository.GetMemberAsync(chatId, query.UserId, ct);
            if (member is not null)
            {
                result.Add(new ChatNotificationSettingsDto
                {
                    ChatId = chatId,
                    NotificationsEnabled = member.NotificationsEnabled
                });
            }
        }

        return Result<List<ChatNotificationSettingsDto>>.Success(result);
    }
}

