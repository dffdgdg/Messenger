namespace API.Application.Features.ReadReceipt.Queries;

public sealed record GetUnreadCountQuery(int UserId, int ChatId);
public sealed record GetAllUnreadCountsQuery(int UserId);
