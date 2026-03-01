namespace MessengerAPI.Mapping;

public static class MessageMappings
{
    public static MessageDto ToDto(this Message message, int? currentUserId = null, IUrlBuilder? urlBuilder = null)
    {
        var isDeleted = message.IsDeleted ?? false;

        return new MessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            Content = isDeleted ? "[Сообщение удалено]" : message.Content,
            CreatedAt = message.CreatedAt,
            EditedAt = message.EditedAt,
            IsEdited = message.EditedAt.HasValue && !isDeleted,
            IsDeleted = isDeleted,
            SenderName = message.Sender?.FormatDisplayName(),
            SenderAvatarUrl = message.Sender?.Avatar.BuildFullUrl(urlBuilder),
            IsOwn = currentUserId.HasValue && message.SenderId == currentUserId,
            ReplyToMessageId = message.ReplyToMessageId,
            ForwardedFromMessageId = message.ForwardedFromMessageId,
            ReplyToMessage = message.ReplyToMessage?.ToReplyPreviewDto(),
            ForwardedFrom = message.ForwardedFromMessage?.ToForwardInfoDto(),
            IsVoiceMessage = message.IsVoiceMessage,
            TranscriptionStatus = isDeleted ? null : message.TranscriptionStatus,
            Files = isDeleted
                ? []
                : message.MessageFiles?.Select(f => f.ToDto(urlBuilder)).ToList() ?? [],
            Poll = isDeleted ? null : message.Polls?.FirstOrDefault()?.ToDto(currentUserId)
        };
    }

    public static MessageReplyPreviewDto ToReplyPreviewDto(this Message message)
    {
        var isDeleted = message.IsDeleted ?? false;

        return new()
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            SenderName = message.Sender?.FormatDisplayName(),
            Content = isDeleted ? "[Сообщение удалено]" : message.Content,
            CreatedAt = message.CreatedAt,
            IsDeleted = isDeleted
        };
    }

    public static MessageForwardInfoDto ToForwardInfoDto(this Message message) => new()
    {
        OriginalMessageId = message.Id,
        OriginalChatId = message.ChatId,
        OriginalSenderId = message.SenderId,
        OriginalSenderName = message.Sender?.FormatDisplayName(),
        OriginalCreatedAt = message.CreatedAt
    };
}