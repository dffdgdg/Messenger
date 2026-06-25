using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Message;

namespace API.Application.Features.Message.Queries;

public class GetMessagesBeforeQueryHandler(
    IMessageRepository messageRepository,
    IChatRepository chatRepository,
    IAccessControlService accessControl,
    IUrlBuilder urlBuilder)
    : IQueryHandler<GetMessagesBeforeQuery, Result<PagedMessagesDto>>
{
    public virtual async Task<Result<PagedMessagesDto>> HandleAsync(GetMessagesBeforeQuery query, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(query.UserId, query.ChatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var cutoff = await GetHistoryCutoffAsync(query.ChatId, query.UserId);
        var messages = await messageRepository.GetBeforeAsync(query.ChatId, query.MessageId, query.Count, cutoff, ct);

        return Result<PagedMessagesDto>.Success(await BuildPagedResultAsync(messages, query.ChatId, query.MessageId,
            query.UserId, cutoff, ct));
    }

    private async Task<PagedMessagesDto> BuildPagedResultAsync(List<Domain.Entities.Message> messages, int chatId, int anchorId,
        int userId, DateTime? cutoff, CancellationToken ct)
    {
        var oldestId = messages.Count > 0 ? messages.Min(m => m.Id) : anchorId;
        var newestId = messages.Count > 0 ? messages.Max(m => m.Id) : anchorId;

        var hasOlder = await messageRepository.HasOlderAsync(chatId, oldestId, cutoff, ct);
        var hasNewer = await messageRepository.HasNewerAsync(chatId, newestId, cutoff, ct);

        return new PagedMessagesDto
        {
            Messages = [.. messages.OrderBy(m => m.Id).Select(m => m.ToDto(userId, urlBuilder))],
            HasMoreMessages = hasOlder,
            HasNewerMessages = hasNewer
        };
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

