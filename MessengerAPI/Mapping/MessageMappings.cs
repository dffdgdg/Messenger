namespace MessengerAPI.Mapping;

public static class MessageMappings
{
    public static MessageDto ToDto(this Message message, int? currentUserId = null, IUrlBuilder? urlBuilder = null)
    {
        var isDeleted = message.IsDeleted ?? false;
        var isSystem = message.IsSystemMessage;
        var voice = message.VoiceMessage;

        return new MessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            Content = isDeleted ? "[Сообщение удалено]" : message.Content,
            CreatedAt = message.CreatedAt,
            EditedAt = message.EditedAt,
            IsEdited = message.EditedAt.HasValue && !isDeleted && !isSystem,
            IsDeleted = isDeleted,
            SenderName = message.Sender?.FormatDisplayName(),
            SenderAvatarUrl = message.Sender?.Avatar.BuildFullUrl(urlBuilder),
            IsOwn = !isSystem && currentUserId.HasValue && message.SenderId == currentUserId,
            IsSystemMessage = isSystem,
            SystemEventType = isSystem ? message.SystemEventType : null,
            TargetUserId = isSystem ? message.TargetUserId : null,
            TargetUserName = isSystem ? message.TargetUser?.FormatDisplayName() : null,

            ReplyToMessageId = message.ReplyToMessageId,
            ForwardedFromMessageId = message.ForwardedFromMessageId,
            ReplyToMessage = message.ReplyToMessage?.ToReplyPreviewDto(),
            ForwardedFrom = message.ForwardedFromMessage?.ToForwardInfoDto(),

            IsVoiceMessage = voice != null,
            VoiceDurationSeconds = voice?.DurationSeconds,
            TranscriptionStatus = isDeleted ? null : voice?.TranscriptionStatus,
            TranscriptionText = isDeleted ? null : voice?.TranscriptionText,
            VoiceFileUrl = isDeleted ? null : voice?.FilePath.BuildFullUrl(urlBuilder),
            VoiceFileName = voice?.FileName,
            VoiceContentType = voice?.ContentType,
            VoiceFileSize = voice?.FileSize,

            Files = isDeleted ? [] : message.MessageFiles?.Select(f => f.ToDto(urlBuilder)).ToList() ?? [],
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