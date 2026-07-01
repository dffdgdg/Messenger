using API.Application.Services.Abstractions;
using API.Domain.Entities;
using Shared.Contracts.Chat;
using Shared.Enum;

namespace API.Application.Mapping;

public static class ChatMappings
{
    public static ChatDto ToDto(this Chat chat, IUrlBuilder? urlBuilder = null) => new()
    {
        Id = chat.Id,
        Name = chat.Name,
        Type = chat.Type,
        CreatedById = chat.CreatedById ?? 0,
        LastMessageDate = chat.LastMessageTime,
        Avatar = AvatarUrlHelper.BuildChatAvatarUrl(urlBuilder, chat.Id, chat.Avatar),
        ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
    };

    public static ChatDto ToDto(this Chat chat, User? dialogPartner, IUrlBuilder? urlBuilder = null)
    {
        var dto = chat.ToDto(urlBuilder);

        if (chat.Type == ChatType.Contact && dialogPartner != null)
        {
            dto.Name = dialogPartner.GetDisplayName();
            dto.Avatar = AvatarUrlHelper.BuildUserAvatarUrl(urlBuilder, dialogPartner.Id, dialogPartner.Avatar);
            dto.ContactUserId = dialogPartner.Id;
            dto.ContactStatusType = dialogPartner.StatusType;
            dto.ContactStatusExpiresAt = dialogPartner.StatusExpiresAt;
        }

        return dto;
    }
}