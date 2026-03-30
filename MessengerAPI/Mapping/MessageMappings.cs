namespace MessengerAPI.Mapping;

public static class MessageMappings
{
    public static MessageDto ToDto(this Message message, int? currentUserId = null, IUrlBuilder? urlBuilder = null)
    {
        var isDeleted = message.IsDeleted ?? false;
        var isSystem = message.IsSystemMessage;
        var voice = message.VoiceMessage;
        var senderName = message.Sender?.FormatDisplayName();
        var targetUserName = isSystem ? message.TargetUser?.FormatDisplayName() : null;
        var resolvedContent = isDeleted
            ? "[Сообщение удалено]"
            : (isSystem
                ? BuildSystemMessageContent(message.SystemEventType, senderName, targetUserName, message.Content)
                : message.Content);

        return new MessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            Content = resolvedContent,
            CreatedAt = message.CreatedAt,
            EditedAt = message.EditedAt,
            IsEdited = message.EditedAt.HasValue && !isDeleted && !isSystem,
            IsDeleted = isDeleted,
            SenderName = senderName,
            SenderAvatarUrl = message.Sender?.Avatar.BuildFullUrl(urlBuilder),
            IsOwn = !isSystem && currentUserId.HasValue && message.SenderId == currentUserId,
            IsSystemMessage = isSystem,
            SystemEventType = isSystem ? message.SystemEventType : null,
            TargetUserId = isSystem ? message.TargetUserId : null,
            TargetUserName = targetUserName,

            ReplyToMessageId = message.ReplyToMessageId,
            ForwardedFromMessageId = message.ForwardedFromMessageId,
            ReplyToMessage = message.ReplyToMessage?.ToReplyPreviewDto(),
            ForwardedFrom = message.ForwardedFromMessage?.ToForwardInfoDto(),

            IsVoiceMessage = voice != null,
            VoiceDurationSeconds = voice?.DurationSeconds,
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
    private static string BuildSystemMessageContent(SystemEventType? eventType, string? senderName, string? targetUserName, string? fallbackContent)
    {
        var actor = string.IsNullOrWhiteSpace(senderName) ? "Пользователь" : senderName;
        var target = string.IsNullOrWhiteSpace(targetUserName) ? "пользователя" : targetUserName;

        return eventType switch
        {
            SystemEventType.ChatCreated => $"{actor} создал(а) группу",
            SystemEventType.MemberAdded => $"{actor} добавил(а) {target}",
            SystemEventType.MemberRemoved => $"{actor} удалил(а) {target}",
            SystemEventType.MemberLeft => $"{actor} покинул(а) группу",
            SystemEventType.RoleChanged => $"{actor} изменил(а) роль участника {target}",
            _ => string.IsNullOrWhiteSpace(fallbackContent) ? "Системное сообщение" : fallbackContent
        };
    }
}