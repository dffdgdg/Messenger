using API.Application.Common;
using API.Application.Configuration;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Microsoft.Extensions.Options;
using Shared.Contracts.Chat;
using Shared.Contracts.Search;
using Shared.Enum;

namespace API.Application.Features.Message.Queries;

public class GlobalSearchQueryHandler(
    IMessageRepository messageRepository,
    IChatRepository chatRepository,
    IAccessControlService accessControl,
    IUrlBuilder urlBuilder,
    IOptions<MessengerSettings> options)
    : IQueryHandler<GlobalSearchQuery, Result<GlobalSearchResponseDto>>
{
    private readonly MessengerSettings settings = options.Value;

    public virtual async Task<Result<GlobalSearchResponseDto>> HandleAsync(GlobalSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = NormalizePagination(query.Dto.Page, query.Dto.PageSize, 50);
        var escaped = EscapeLike(query.Dto.Query);
        var hasQuery = !string.IsNullOrWhiteSpace(query.Dto.Query);

        var chatIds = await accessControl.GetUserChatIdsAsync(query.UserId);
        if (chatIds.Count == 0)
            return Result<GlobalSearchResponseDto>.Success(new GlobalSearchResponseDto
            {
                Chats = [],
                Messages = [],
                CurrentPage = query.Dto.Page
            });

        if (query.Dto.FilterChatId.HasValue)
            chatIds = [.. chatIds.Where(id => id == query.Dto.FilterChatId.Value)];

        var historyFilter = await chatRepository
            .GetHistoryRestrictionsAsync(chatIds, query.UserId, ct);

        var chats = hasQuery && query.Dto.FilterChatId is null
            ? await SearchChatsAsync(chatIds, escaped, query.UserId, ct)
            : [];

        var (messages, total, hasMore) = await SearchMessagesAsync(
            chatIds, escaped, query.UserId, query.Dto, page, pageSize, historyFilter, ct);

        return Result<GlobalSearchResponseDto>.Success(new GlobalSearchResponseDto
        {
            Chats = chats,
            Messages = messages,
            TotalChatsCount = chats.Count,
            TotalMessagesCount = total,
            CurrentPage = page,
            HasMoreMessages = hasMore
        });
    }

    private async Task<List<ChatDto>> SearchChatsAsync(List<int> chatIds, string query, int userId, CancellationToken ct)
    {
        const int max = 5;
        var result = new List<ChatDto>();

        var dialogs = await chatRepository.GetContactChatsWithMembersAsync(chatIds, ct);
        foreach (var chat in dialogs)
        {
            var partner = chat.ChatMembers.FirstOrDefault(cm => cm.UserId != userId)?.User;
            if (partner is null) continue;

            var name = partner.GetDisplayName();
            if (string.IsNullOrEmpty(query)
                || name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (partner.Username ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ChatDto
                {
                    Id = chat.Id,
                    Name = name,
                    Type = chat.Type,
                    Avatar = urlBuilder.BuildUrl(partner.Avatar),
                    LastMessageDate = chat.LastMessageTime
                });
            }
        }

        var groups = await chatRepository.SearchGroupChatsAsync(chatIds, query, max, ct);
        result.AddRange(groups.Select(c => c.ToDto(urlBuilder)));

        return [.. result.Take(max)];
    }

    private async Task<(List<GlobalSearchMessageDto>, int Total, bool HasMore)> SearchMessagesAsync(
        List<int> chatIds, string escaped, int userId,
        GlobalSearchQueryDto dto, int page, int pageSize,
        Dictionary<int, DateTime> historyFilter, CancellationToken ct)
    {
        var (items, total) = await messageRepository.SearchGlobalAsync(
            chatIds, escaped,
            dto.SenderId, dto.DateFrom, dto.DateTo,
            dto.HasFiles == true, dto.HasVoice == true,
            dto.HasPoll == true, dto.OnlyText == true,
            dto.OldestFirst, page, pageSize, historyFilter, ct);

        var dialogIds = items
            .Where(m => m.Chat.Type == ChatType.Contact)
            .Select(m => m.ChatId)
            .Distinct()
            .ToList();

        var partners = await GetDialogPartnersAsync(dialogIds, userId, ct);
        var dtos = items.ConvertAll(m => BuildSearchDto(m, escaped, partners));

        return (dtos, total, total > ((page - 1) * pageSize) + pageSize);
    }

    private async Task<Dictionary<int, (string Name, string? Avatar)>> GetDialogPartnersAsync(
        List<int> chatIds, int userId, CancellationToken ct)
    {
        if (chatIds.Count == 0) return [];

        var partners = await chatRepository.GetDialogPartnersAsync(chatIds, userId, ct);
        return partners.ToDictionary(
            p => p.ChatId,
            p => (FormatName(p.Surname, p.Name, p.Midname), urlBuilder.BuildUrl(p.Avatar)));
    }

    private GlobalSearchMessageDto BuildSearchDto(
        UserMessage m,
        string term,
        Dictionary<int, (string Name, string? Avatar)> partners)
    {
        var dto = new GlobalSearchMessageDto
        {
            Id = m.Id,
            ChatId = m.ChatId,
            ChatType = m.Chat.Type,
            SenderId = m.SenderId,
            SenderName = m.Sender?.GetDisplayName(),
            Content = m.Content,
            CreatedAt = m.CreatedAt,
            HighlightedContent = Highlight(m.Content, term),
            HasFiles = m.MessageFiles?.Count > 0,
            HasVoice = m.VoiceMessage != null,
            HasPoll = m.Poll != null
        };

        (dto.ChatName, dto.ChatAvatar) =
            m.Chat.Type == ChatType.Contact && partners.TryGetValue(m.ChatId, out var p)
                ? p
                : (m.Chat.Name, urlBuilder.BuildUrl(m.Chat.Avatar));

        return dto;
    }

    private static string? Highlight(string? content, string term)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(term)) return content;

        var idx = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return content.Length > 100 ? content[..100] + "..." : content;

        const int ctx = 40;
        var start = Math.Max(0, idx - ctx);
        var end = Math.Min(content.Length, idx + term.Length + ctx);
        return (start > 0 ? "..." : "") + content[start..end] + (end < content.Length ? "..." : "");
    }

    private static string FormatName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : "Без имени";
    }

    private static (int Page, int PageSize) NormalizePagination(int page, int pageSize, int max)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, max));

    private static string EscapeLike(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;
        return pattern.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}

