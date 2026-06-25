using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Shared.Contracts.Message;

namespace API.Application.Features.Message.Queries;

public class GetMessagesAroundQueryHandler(IMessageRepository messageRepository, IChatRepository chatRepository,
    IAccessControlService accessControl, IUrlBuilder urlBuilder)
    : IQueryHandler<GetMessagesAroundQuery, Result<PagedMessagesDto>>
{
    public virtual async Task<Result<PagedMessagesDto>> HandleAsync(GetMessagesAroundQuery query, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(query.UserId, query.ChatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var cutoff = await GetHistoryCutoffAsync(query.ChatId, query.UserId);
        var half = query.Count / 2;

        var before = await messageRepository.GetBeforeAsync(query.ChatId, query.MessageId, half + 1, cutoff, ct);
        var after = await messageRepository.GetAfterAsync(query.ChatId, query.MessageId, half, cutoff, ct);
        var anchor = await messageRepository.FindUserMessageWithIncludesNoTrackingAsync(query.MessageId, ct);

        var allMessages = before.Concat(after);
        if (anchor != null) allMessages = allMessages.Append(anchor);

        var ordered = allMessages.GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.Id).ToList();

        var window = SliceWindow(ordered, query.MessageId, half, query.Count);

        var oldestId = window.Count > 0 ? window[0].Id : query.MessageId;
        var newestId = window.Count > 0 ? window[^1].Id : query.MessageId;

        var hasOlder = await messageRepository.HasOlderAsync(query.ChatId, oldestId, cutoff, ct);
        var hasNewer = await messageRepository.HasNewerAsync(query.ChatId, newestId, cutoff, ct);

        return Result<PagedMessagesDto>.Success(new PagedMessagesDto
        {
            Messages = window.ConvertAll(m => m.ToDto(query.UserId, urlBuilder)),
            HasMoreMessages = hasOlder,
            HasNewerMessages = hasNewer
        });
    }

    private static List<Domain.Entities.Message> SliceWindow(List<Domain.Entities.Message> ordered, int anchorId, int half, int count)
    {
        var anchorIdx = ordered.FindIndex(m => m.Id == anchorId);
        if (anchorIdx < 0)
            return [.. ordered.Take(count)];

        var start = Math.Max(0, anchorIdx - half);
        var end = Math.Min(ordered.Count, anchorIdx + half + 1);
        return ordered[start..end];
    }

    private async Task<DateTime?> GetHistoryCutoffAsync(int chatId, int userId)
    {
        var showHistory = await chatRepository.GetShowHistoryForNewMembersAsync(chatId);
        if (showHistory != false) return null;

        var role = await accessControl.GetRoleAsync(userId, chatId);
        if (role is Shared.Enum.ChatRole.Owner or Shared.Enum.ChatRole.Admin)
            return null;

        var member = await accessControl.GetChatMemberAsync(userId, chatId);
        return member?.JoinedAt ?? DateTime.MinValue;
    }
}

