using Shared.Contracts.Search;

namespace API.Application.Features.Message.Queries;

public sealed record GetLatestMessagesQuery(int ChatId, int UserId, int Take);
public sealed record GetMessagesAroundQuery(int ChatId, int MessageId, int UserId, int Count);
public sealed record GetMessagesBeforeQuery(int ChatId, int MessageId, int UserId, int Count);
public sealed record GetMessagesAfterQuery(int ChatId, int MessageId, int UserId, int Count);
public sealed record GetPinnedMessagesQuery(int ChatId, int UserId);
public sealed record GetChatCountsQuery(int ChatId, int UserId);
public sealed record SearchMessagesQuery(int ChatId, int UserId, SearchMessagesQueryDto Dto);
public sealed record GlobalSearchQuery(int UserId, GlobalSearchQueryDto Dto);
