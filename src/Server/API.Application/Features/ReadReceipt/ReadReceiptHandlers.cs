using API.Application.Common;
using API.Application.Features.ReadReceipt.Commands;
using API.Application.Features.ReadReceipt.Queries;
using API.Domain.Common;
using Shared.Contracts.ReadReceipt;

namespace API.Application.Features.ReadReceipt;

public class ReadReceiptHandlers(
    MarkAsReadCommandHandler markAsRead,
    GetAllUnreadCountsQueryHandler getAllUnreadCounts,
    GetUnreadCountQueryHandler getUnreadCounts)
    : IReadReceiptHandlers
{
    public ICommandHandler<MarkAsReadCommand, ReadReceiptResponseDto> MarkAsRead => markAsRead;
    public IQueryHandler<GetAllUnreadCountsQuery, Result<AllUnreadCountsDto>> GetAllUnreadCounts => getAllUnreadCounts;
    public IQueryHandler<GetUnreadCountQuery, Result<int>> GetUnreadCounts => getUnreadCounts;
}