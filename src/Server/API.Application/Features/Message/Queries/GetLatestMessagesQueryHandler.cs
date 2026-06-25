using API.Application.Common;
using API.Application.Configuration;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shared.Contracts.Message;

namespace API.Application.Features.Message.Queries;

public class GetLatestMessagesQueryHandler(
    IMessageRepository messageRepository,
    IChatRepository chatRepository,
    IAccessControlService accessControl,
    IUrlBuilder urlBuilder,
    IMemoryCache cache,
    IOptions<MessengerSettings> options)
    : IQueryHandler<GetLatestMessagesQuery, Result<PagedMessagesDto>>
{
    private readonly MessengerSettings settings = options.Value;
    public virtual async Task<Result<PagedMessagesDto>> HandleAsync(GetLatestMessagesQuery query, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(query.UserId, query.ChatId);
        if (access.IsFailure) return access.As<PagedMessagesDto>();

        var take = Math.Clamp(query.Take, 1, settings.MaxPageSize);
        var cutoff = await GetHistoryCutoffAsync(query.ChatId, query.UserId);

        var cacheKey = $"latest_msg_{query.ChatId}_{take}_{cutoff?.Ticks ?? 0}";
        if (cache.TryGetValue(cacheKey, out PagedMessagesDto? cached) && cached is not null)
            return Result<PagedMessagesDto>.Success(cached);

        var (messages, hasOlder) = await messageRepository.GetLatestAsync(query.ChatId, take, cutoff, ct);

        var dtos = messages.OrderBy(m => m.Id).Select(m => m.ToDto(query.UserId, urlBuilder)).ToList();

        var result = new PagedMessagesDto
        {
            Messages = dtos,
            HasMoreMessages = hasOlder,
            HasNewerMessages = false
        };

        cache.Set(cacheKey, result, TimeSpan.FromSeconds(1));

        return Result<PagedMessagesDto>.Success(result);
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

