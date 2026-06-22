using API.Application.Services.Abstractions;
using API.Application.Services.Features.Chat;
using API.Domain.Entities;
using Shared.Dto.Message;

namespace API.Application.Mapping;

public static class MessageMappings
{
    private const string DeletedMessagePlaceholder = "[Сообщение удалено]";

    public static MessageDto ToDto(this Message message, int? currentUserId = null, IUrlBuilder? urlBuilder = null)
    {
        var isDeleted = message.IsDeleted ?? false;
        var isPinnedAndVisible = message.PinnedAt != null && !isDeleted;

        if (message is SystemMessage sys)
        {
            var initiatorName = sys.Initiator?.GetDisplayName();
            var targetName = sys.TargetUser?.GetDisplayName();

            return new MessageDto
            {
                Id = sys.Id,
                ChatId = sys.ChatId,
                SenderId = sys.InitiatorId,
                Content = SystemMessageFormatter.Format(sys.SystemEventType, initiatorName, targetName),
                CreatedAt = sys.CreatedAt,
                IsDeleted = isDeleted,
                IsSystemMessage = true,
                SystemEventType = sys.SystemEventType,
                TargetUserId = sys.TargetUserId,
                TargetUserName = targetName,
                SenderName = initiatorName,
                IsPinned = isPinnedAndVisible,
                PinnedAt = isPinnedAndVisible ? sys.PinnedAt : null,
                PinnedByUserId = isPinnedAndVisible ? sys.PinnedByUserId : null,
                IsOwn = false,
                Files = [],
            };
        }

        var user = (UserMessage)message;
        var senderName = user.Sender?.GetDisplayName();
        var voice = ResolveInForwardChain(user, m => m.VoiceMessage);
        var resolvedFiles = ResolveInForwardChain(user, m => m.MessageFiles?.Count > 0 ? m.MessageFiles : null);
        var resolvedPoll = ResolveInForwardChain(user, m => m.Poll);
        var resolvedContent = ResolveContentInForwardChain(user);

        return new MessageDto
        {
            Id = user.Id,
            ChatId = user.ChatId,
            SenderId = user.SenderId,
            Content = isDeleted ? DeletedMessagePlaceholder : resolvedContent ?? string.Empty,
            CreatedAt = user.CreatedAt,
            EditedAt = user.EditedAt,
            IsEdited = user.EditedAt.HasValue && !isDeleted,
            IsDeleted = isDeleted,
            IsSystemMessage = false,
            IsPinned = isPinnedAndVisible,
            PinnedAt = isPinnedAndVisible ? user.PinnedAt : null,
            PinnedByUserId = isPinnedAndVisible ? user.PinnedByUserId : null,
            SenderName = senderName,
            SenderAvatarUrl = urlBuilder?.BuildUrl(user.Sender?.Avatar),
            IsOwn = currentUserId.HasValue && user.SenderId == currentUserId,

            ReplyToMessageId = user.ReplyToMessageId,
            ForwardedFromMessageId = user.ForwardedFromMessageId,
            ReplyToMessage = user.ReplyToMessage?.ToReplyPreviewDto(),
            ForwardedFrom = user.ForwardedFromMessage?.ToForwardInfoDto(),

            IsVoiceMessage = voice != null,
            VoiceDurationSeconds = voice?.DurationSeconds,
            VoiceWaveform = voice?.Waveform,
            VoiceFileUrl = isDeleted ? null : urlBuilder?.BuildUrl(voice?.FilePath),
            VoiceFileSize = voice?.FileSize,

            Files = isDeleted ? [] : resolvedFiles?.Select(f => f.ToDto(urlBuilder)).ToList() ?? [],
            Poll = isDeleted ? null : resolvedPoll?.ToDto(currentUserId)
        };
    }

    public static MessageReplyPreviewDto ToReplyPreviewDto(this Message message)
    {
        var isDeleted = message.IsDeleted ?? false;

        if (message is SystemMessage sys)
        {
            return new MessageReplyPreviewDto
            {
                Id = sys.Id,
                ChatId = sys.ChatId,
                SenderId = sys.InitiatorId,
                SenderName = sys.Initiator?.GetDisplayName(),
                Content = SystemMessageFormatter.Format(sys.SystemEventType, sys.Initiator?.GetDisplayName(), sys.TargetUser?.GetDisplayName()),
                CreatedAt = sys.CreatedAt,
                IsDeleted = isDeleted
            };
        }

        var user = (UserMessage)message;
        return new MessageReplyPreviewDto
        {
            Id = user.Id,
            ChatId = user.ChatId,
            SenderId = user.SenderId,
            SenderName = user.Sender?.GetDisplayName(),
            Content = isDeleted ? null : user.Content,
            CreatedAt = user.CreatedAt,
            IsDeleted = isDeleted,
            IsVoiceMessage = user.VoiceMessage != null,
            HasPoll = user.Poll != null,
            FilesCount = user.MessageFiles?.Count ?? 0
        };
    }

    private static string? ResolveContentInForwardChain(UserMessage message)
    {
        UserMessage? current = message;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Content))
                return current.Content;

            current = current.ForwardedFromMessage;
        }

        return null;
    }

    private static T? ResolveInForwardChain<T>(UserMessage message, Func<UserMessage, T?> selector)
        where T : class
    {
        UserMessage? current = message;
        while (current is not null)
        {
            var value = selector(current);
            if (value is not null)
                return value;

            current = current.ForwardedFromMessage;
        }

        return null;
    }

    public static MessageForwardInfoDto ToForwardInfoDto(this Message message)
    {
        if (message is UserMessage user)
        {
            return new MessageForwardInfoDto
            {
                OriginalMessageId = user.Id,
                OriginalChatId = user.ChatId,
                OriginalSenderId = user.SenderId,
                OriginalSenderName = user.Sender?.GetDisplayName(),
                OriginalCreatedAt = user.CreatedAt
            };
        }

        var sys = (SystemMessage)message;
        return new MessageForwardInfoDto
        {
            OriginalMessageId = sys.Id,
            OriginalChatId = sys.ChatId,
            OriginalSenderId = sys.InitiatorId,
            OriginalSenderName = sys.Initiator?.GetDisplayName(),
            OriginalCreatedAt = sys.CreatedAt
        };
    }
}