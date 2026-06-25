using API.Application.Common;
using API.Domain.Common;
using Shared.Contracts.Chat;
using Shared.Enum;

namespace API.Application.Features.Chat.Queries;

public class GetUserGroupsQueryHandler(GetUserChatsQueryHandler chatsHandler)
    : IQueryHandler<GetUserGroupsQuery, Result<List<ChatDto>>>
{
    public virtual async Task<Result<List<ChatDto>>> HandleAsync(GetUserGroupsQuery query, CancellationToken ct = default)
    {
        var all = await chatsHandler.HandleAsync(new GetUserChatsQuery(query.UserId), ct);
        if (all.IsFailure) return all;

        return Result<List<ChatDto>>.Success(all.Value!.Where(c => c.Type != ChatType.Contact).ToList());
    }
}

