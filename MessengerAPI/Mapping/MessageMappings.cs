using MessengerAPI.Services.Chat;

namespace MessengerAPI.Mapping;

public static class MessageMappings
{
    private const string DeletedMessagePlaceholder = "[Сообщение удалено]";

    public static MessageDto ToDto(this Message message, int? currentUserId = null, IUrlBuilder? urlBuilder = null)
    {
        var isDeleted = message.IsDeleted ?? false;
        var isSystem = message.IsSystemMessage;
        var isPinnedAndVisible = message.IsPinned && !isDeleted;
        var senderName = message.Sender?.FormatDisplayName();
        var targetUserName = isSystem ? message.TargetUser?.FormatDisplayName() : null;
        var voice = message.VoiceMessage;

        return new MessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            Content = ResolveContent(message, isDeleted, isSystem, senderName, targetUserName),
            CreatedAt = message.CreatedAt,
            EditedAt = message.EditedAt,
            IsEdited = message.EditedAt.HasValue && !isDeleted && !isSystem,
            IsDeleted = isDeleted,
            IsPinned = isPinnedAndVisible,
            PinnedAt = isPinnedAndVisible ? message.PinnedAt : null,
            PinnedByUserId = isPinnedAndVisible ? message.PinnedByUserId : null,
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

        return new MessageReplyPreviewDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            SenderName = message.Sender?.FormatDisplayName(),
            Content = isDeleted ? DeletedMessagePlaceholder : message.Content,
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

    private static string ResolveContent(Message message, bool isDeleted, bool isSystem, string? senderName, string? targetName)
    {
        if (isDeleted) return DeletedMessagePlaceholder;
        if (isSystem) return SystemMessageFormatter.Format(message.SystemEventType, senderName, targetName, message.Content);
        return message.Content ?? string.Empty;
    }
}