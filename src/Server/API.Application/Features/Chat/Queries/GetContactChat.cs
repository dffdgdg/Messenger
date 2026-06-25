using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Chat;

namespace API.Application.Features.Chat.Queries;

public class GetContactChatQueryHandler(IChatRepository chatRepository, IUrlBuilder urlBuilder)
    : IQueryHandler<GetContactChatQuery, Result<ChatDto>>
{
    public virtual async Task<Result<ChatDto>> HandleAsync(GetContactChatQuery query, CancellationToken ct = default)
    {
        var chat = await chatRepository.FindContactChatAsync(query.UserId, query.ContactUserId, ct);

        if (chat is null)
            return Result<ChatDto>.NotFound("Диалог не найден");

        var dto = chat.ToDto(urlBuilder);

        var partners = await chatRepository.GetDialogPartnersAsync([chat.Id], query.UserId, ct);
        var partner = partners.FirstOrDefault();

        if (partner is not null)
        {
            dto.Name = FormatDisplayName(partner.Surname, partner.Name, partner.Midname);
            dto.Avatar = urlBuilder.BuildUrl(partner.Avatar);
        }

        return Result<ChatDto>.Success(dto);
    }

    private static string FormatDisplayName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : "Без имени";
    }
}

