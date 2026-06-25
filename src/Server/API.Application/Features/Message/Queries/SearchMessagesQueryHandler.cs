using API.Application.Common;
using API.Application.Configuration;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Microsoft.Extensions.Options;
using Shared.Contracts.Message;
using Shared.Contracts.Search;

namespace API.Application.Features.Message.Queries;

public class SearchMessagesQueryHandler(
    IMessageRepository messageRepository,
    IChatRepository chatRepository,
    IAccessControlService accessControl,
    IUrlBuilder urlBuilder,
    IOptions<MessengerSettings> options)
    : IQueryHandler<SearchMessagesQuery, Result<SearchMessagesResponseDto>>
{
    private readonly MessengerSettings settings = options.Value;
    public virtual async Task<Result<SearchMessagesResponseDto>> HandleAsync(SearchMessagesQuery query, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(query.UserId, query.ChatId);
        if (access.IsFailure) return access.As<SearchMessagesResponseDto>();

        var (page, pageSize) = NormalizePagination(query.Dto.Page, query.Dto.PageSize);
        var escaped = EscapeLike(query.Dto.Query);
        var cutoff = await MessageHistoryHelper.GetHistoryCutoffAsync(query.ChatId, query.UserId, chatRepository, accessControl);

        var (items, total) = await messageRepository.SearchInChatAsync(
            query.ChatId, escaped,
            query.Dto.SenderId, query.Dto.DateFrom, query.Dto.DateTo,
            query.Dto.HasFiles == true, query.Dto.HasVoice == true,
            query.Dto.HasPoll == true, query.Dto.OnlyText == true,
            query.Dto.OldestFirst, page, pageSize, cutoff, ct);

        var ordered = query.Dto.OldestFirst ? items.AsEnumerable() : items.AsEnumerable().Reverse();

        return Result<SearchMessagesResponseDto>.Success(new SearchMessagesResponseDto
        {
            Messages = [.. ordered.Select(m => m.ToDto(query.UserId, urlBuilder))],
            TotalCount = total,
            CurrentPage = page,
            HasMoreMessages = total > ((page - 1) * pageSize) + pageSize
        });
    }

    private (int Page, int PageSize) NormalizePagination(int page, int pageSize)
    {
        var np = Math.Max(1, page);
        var nps = Math.Clamp(pageSize, 1, settings.MaxPageSize);
        return (np, nps);
    }

    private static string EscapeLike(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;
        return pattern.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}

