namespace MessengerAPI.Mapping;

public static class ChatMappings
{
    public static ChatDto ToDto(this Chat chat, IUrlBuilder? urlBuilder = null) => new()
        {
            Id = chat.Id,
            Name = chat.Name,
            Type = chat.Type,
            CreatedById = chat.CreatedById ?? 0,
            LastMessageDate = chat.LastMessageTime,
            Avatar = chat.Avatar.BuildFullUrl(urlBuilder)
        };

    public static ChatDto ToDto(this Chat chat, User? dialogPartner, IUrlBuilder? urlBuilder = null)
    {
        var dto = chat.ToDto(urlBuilder);

        if (chat.Type == ChatType.Contact && dialogPartner != null)
        {
            dto.Name = dialogPartner.FormatDisplayName();
            dto.Avatar = dialogPartner.Avatar.BuildFullUrl(urlBuilder);
        }

        return dto;
    }
}