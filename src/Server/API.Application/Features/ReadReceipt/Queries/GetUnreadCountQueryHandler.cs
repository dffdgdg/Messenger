using API.Application.Common;
using API.Domain.Common;
using API.Domain.Repositories;

namespace API.Application.Features.ReadReceipt.Queries;

public class GetUnreadCountQueryHandler(IReadReceiptRepository readReceiptRepository)
    : IQueryHandler<GetUnreadCountQuery, Result<int>>
{
    public virtual async Task<Result<int>> HandleAsync(GetUnreadCountQuery query, CancellationToken ct = default)
    {
        var member = await readReceiptRepository.FindMemberReadonlyAsync(query.ChatId, query.UserId, ct);

        if (member is null)
            return Result<int>.Success(0);

        var count = await readReceiptRepository.CountUnreadAsync(query.ChatId, query.UserId, member.LastReadMessageId ?? 0, ct);

        return Result<int>.Success(count);
    }
}

