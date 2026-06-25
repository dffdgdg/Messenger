using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Projections;
using API.Domain.Repositories;
using Shared.Contracts.Chat;
using Shared.Enum;

namespace API.Application.Features.Chat.Queries;

public class GetChatForUserQueryHandler(IAccessControlService accessControl, IChatRepository chatRepository,
    IOnlineUserService onlineService, IUrlBuilder urlBuilder)
    : IQueryHandler<GetChatForUserQuery, Result<ChatDto>>
{
    public virtual async Task<Result<ChatDto>> HandleAsync(GetChatForUserQuery query, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(query.UserId, query.ChatId);
        if (access.IsFailure) return access.As<ChatDto>();

        var chat = await chatRepository.FindByIdAsync(query.ChatId, ct);
        if (chat is null)
            return Result<ChatDto>.NotFound($"Чат с ID {query.ChatId} не найден");

        var member = await chatRepository.GetMemberAsync(query.ChatId, query.UserId, ct);

        var dto = new ChatDto
        {
            Id = chat.Id,
            Type = chat.Type,
            CreatedById = chat.CreatedById ?? 0,
            LastMessageDate = chat.LastMessageTime,
            ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers,
            CurrentUserRole = member?.Role ?? default
        };

        if (chat.Type == ChatType.Contact)
        {
            var partners = await chatRepository.GetDialogPartnersAsync([query.ChatId], query.UserId, ct);
            var partner = partners.FirstOrDefault();

            if (partner is not null)
            {
                var isOnline = onlineService.FilterOnline([partner.UserId]).Count != 0;
                dto.Name = FormatDisplayName(partner.Surname, partner.Name, partner.Midname);
                dto.Avatar = urlBuilder.BuildUrl(partner.Avatar);
                dto.ContactUserId = partner.UserId;
                dto.ContactIsOnline = isOnline;
                dto.ContactStatusType = partner.StatusType;
                dto.ContactStatusExpiresAt = partner.StatusExpiresAt;
            }
        }
        else
        {
            dto.Name = chat.Name;
            dto.Avatar = urlBuilder.BuildUrl(chat.Avatar);
        }

        return Result<ChatDto>.Success(dto);
    }

    private static string FormatDisplayName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : "Без имени";
    }
}

