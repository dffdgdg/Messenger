using API.Application.Common;
using API.Application.Features.ReadReceipt.Commands;
using API.Application.Features.ReadReceipt.Queries;
using API.Domain.Common;
using Shared.Contracts.ReadReceipt;

namespace API.Application.Features.ReadReceipt;

public interface IReadReceiptHandlers
{
    ICommandHandler<MarkAsReadCommand, ReadReceiptResponseDto> MarkAsRead { get; }
    IQueryHandler<GetAllUnreadCountsQuery, Result<AllUnreadCountsDto>> GetAllUnreadCounts { get; }
    IQueryHandler<GetUnreadCountQuery, Result<int>> GetUnreadCounts { get; }
}