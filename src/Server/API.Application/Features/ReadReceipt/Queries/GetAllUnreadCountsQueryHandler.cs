using API.Application.Common;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.ReadReceipt;

namespace API.Application.Features.ReadReceipt.Queries;

public class GetAllUnreadCountsQueryHandler(IReadReceiptRepository readReceiptRepository)
    : IQueryHandler<GetAllUnreadCountsQuery, Result<AllUnreadCountsDto>>
{
    public virtual async Task<Result<AllUnreadCountsDto>> HandleAsync(GetAllUnreadCountsQuery query, CancellationToken ct = default)
    {
        var projections = await readReceiptRepository.GetAllUnreadCountsAsync(query.UserId, ct);

        var result = projections.ConvertAll(x => new UnreadCountDto(x.ChatId, x.UnreadCount));

        return Result<AllUnreadCountsDto>.Success(new AllUnreadCountsDto
        {
            Chats = result,
            TotalUnread = result.Sum(x => x.UnreadCount)
        });
    }
}

