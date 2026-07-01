using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Shared.Contracts.Message;
using Shared.Contracts.Notification;
using Shared.Enum;
using Shared.HubProtocol;
using System.Text.RegularExpressions;

namespace API.Application.Features.Message.Commands;

public partial class CreateMessageCommandHandler(
    IUnitOfWork unitOfWork,
    IChatRepository chatRepository,
    IMessageRepository messageRepository,
    IReadReceiptRepository readReceiptRepository,
    IAccessControlService accessControl,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder,
    IPendingUploadStore pendingUploadStore,
    AppDateTime appDateTime)
    : ICommandHandler<CreateMessageCommand, MessageDto>
{
    [GeneratedRegex(@"(?<![a-z0-9_])@([a-z0-9_]{3,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    public virtual async Task<Result<MessageDto>> HandleAsync(CreateMessageCommand command, CancellationToken ct = default)
    {
        var (senderId, request) = (command.SenderId, command.Request);

        var access = await accessControl.EnsureMemberOfAsync(senderId, request.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        var contentValidation = ValidateContent(request);
        if (contentValidation.IsFailure) return contentValidation.As<MessageDto>();

        var refCheck = await ValidateReferencesAsync(request, ct);
        if (refCheck.IsFailure) return refCheck.As<MessageDto>();

        var resolvedForwardId = request.ForwardedFromMessageId.HasValue
            ? await ResolveRootForwardedIdAsync(request.ForwardedFromMessageId.Value, ct)
            : null;

        var buildResult = await BuildMessageAsync(request, senderId, resolvedForwardId, ct);
        if (buildResult.IsFailure) return buildResult.As<MessageDto>();

        var message = buildResult.Value!;

        messageRepository.Add(message);
        await chatRepository.UpdateLastMessageTimeAsync(request.ChatId, appDateTime.UtcNow, ct);

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<MessageDto>();

        var created = await messageRepository.FindUserMessageWithIncludesAsync(message.Id, ct);
        if (created is null)
            return Result<MessageDto>.Internal("Не удалось загрузить созданное сообщение");

        var dto = created.ToDto(senderId, urlBuilder);

        await hubNotifier.SendToChatAsync(request.ChatId, HubMethods.Chat.ReceiveMessage, dto);
        await NotifyMembersAsync(dto, ct);

        return Result<MessageDto>.Success(dto);
    }

    #region Validation

    private static Result ValidateContent(CreateMessageRequest request)
    {
        if (request.IsVoiceMessage || request.ForwardedFromMessageId.HasValue)
            return Result.Success();

        if (string.IsNullOrWhiteSpace(request.Content) && request.Files is not { Count: > 0 })
            return Result.Failure("Сообщение должно содержать текст или файлы");

        return Result.Success();
    }

    private async Task<Result> ValidateReferencesAsync(CreateMessageRequest request, CancellationToken ct)
    {
        if (request.ReplyToMessageId.HasValue)
        {
            var exists = await messageRepository.ExistsInChatAsync(request.ReplyToMessageId.Value, request.ChatId, ct);
            if (!exists)
                return Result.NotFound("Сообщение для ответа не найдено в этом чате");
        }

        if (request.ForwardedFromMessageId.HasValue)
        {
            var exists = await messageRepository.ExistsAsync(request.ForwardedFromMessageId.Value, ct);
            if (!exists)
                return Result.NotFound("Оригинальное сообщение для пересылки не найдено");
        }

        return Result.Success();
    }

    #endregion

    #region Message building

    private async Task<Result<UserMessage>> BuildMessageAsync(CreateMessageRequest request, int senderId, int? resolvedForwardId, CancellationToken ct)
    {
        var content = request.IsVoiceMessage ? null : request.Content;

        if (resolvedForwardId.HasValue && string.IsNullOrWhiteSpace(content))
        {
            var forwarded = await messageRepository.FindUserMessageByIdAsync(resolvedForwardId.Value, ct);
            if (forwarded?.Content is { Length: > 0 })
                content = forwarded.Content;
        }

        var message = new UserMessage
        {
            ChatId = request.ChatId,
            SenderId = senderId,
            Content = content,
            IsDeleted = false,
            ReplyToMessageId = request.ReplyToMessageId,
            ForwardedFromMessageId = resolvedForwardId
        };

        if (request.IsVoiceMessage)
        {
            var attachVoice = AttachVoice(message, request, senderId);
            if (attachVoice.IsFailure) return attachVoice.As<UserMessage>();
        }

        if (request.Files is { Count: > 0 })
        {
            var attach = AttachFiles(message, request, senderId);
            if (attach.IsFailure) return attach.As<UserMessage>();
        }

        return Result<UserMessage>.Success(message);
    }

    /// <summary>
    /// Голосовое, как и обычные файлы, резолвится ТОЛЬКО через одноразовый
    /// токен, выданный при аплоаде через тот же upload-эндпоинт.
    /// Path/FileSize берутся из серверной записи pending-upload, а не из
    /// полей, присланных клиентом.
    /// </summary>
    private Result AttachVoice(UserMessage message, CreateMessageRequest request, int senderId)
    {
        if (string.IsNullOrWhiteSpace(request.VoiceUploadToken))
            return Result.Failure("Голосовое сообщение не было загружено корректно");

        if (!pendingUploadStore.TryConsume(request.VoiceUploadToken, senderId, request.ChatId, out var pending) || pending is null)
            return Result.Failure("Голосовое сообщение не найдено или срок его загрузки истёк");

        message.VoiceMessage = new VoiceMessage
        {
            DurationSeconds = request.VoiceDurationSeconds ?? 0,
            Waveform = request.VoiceWaveform,
            FilePath = pending.RelativePath,
            FileSize = pending.FileSize
        };

        return Result.Success();
    }

    /// <summary>
    /// Файлы резолвятся ТОЛЬКО через одноразовый токен, выданный при аплоаде.
    /// FileName/ContentType/Path берутся из серверной записи pending-upload,
    /// а не из полей, присланных клиентом — иначе клиент мог бы подделать
    /// произвольный путь/тип и "подсунуть" чужой файл в своё сообщение.
    /// </summary>
    private Result AttachFiles(UserMessage message, CreateMessageRequest request, int senderId)
    {
        foreach (var f in request.Files!)
        {
            if (string.IsNullOrWhiteSpace(f.UploadToken))
                return Result.Failure("Файл не был загружен корректно");

            if (!pendingUploadStore.TryConsume(f.UploadToken, senderId, request.ChatId, out var pending) || pending is null)
                return Result.Failure("Файл не найден или срок его загрузки истёк");

            message.MessageFiles.Add(new MessageFile
            {
                FileName = pending.FileName,
                ContentType = pending.ContentType,
                Path = pending.RelativePath
            });
        }

        return Result.Success();
    }

    private async Task<int?> ResolveRootForwardedIdAsync(int messageId, CancellationToken ct)
    {
        var visited = new HashSet<int>();
        int? currentId = messageId;

        while (currentId.HasValue && visited.Add(currentId.Value))
        {
            var current = await messageRepository.FindUserMessageByIdAsync(currentId.Value, ct);

            if (current is null) return messageId;
            if (!current.ForwardedFromMessageId.HasValue) return current.Id;

            currentId = current.ForwardedFromMessageId;
        }

        return messageId;
    }

    #endregion

    #region Notifications

    private async Task NotifyMembersAsync(MessageDto message, CancellationToken ct)
    {
        try
        {
            var mentionedUsernames = ExtractMentions(message.Content);
            var members = await chatRepository.GetMembersForNotificationAsync(message.ChatId, message.SenderId, ct);
            var memberIds = members.ConvertAll(m => m.UserId);

            var chat = await chatRepository.FindByIdAsync(message.ChatId, ct);
            var isContact = chat?.Type == ChatType.Contact;

            var unreadCounts = await readReceiptRepository.GetUnreadCountsForUsersAsync(message.ChatId, memberIds, ct);

            foreach (var m in members)
            {
                var unread = unreadCounts.GetValueOrDefault(m.UserId, 0);

                await hubNotifier.SendToUserAsync(m.UserId, HubMethods.Chat.UnreadCountUpdated, message.ChatId, unread);

                if (!m.GlobalNotificationsEnabled)
                    continue;

                var isMentioned = !string.IsNullOrWhiteSpace(m.Username) && mentionedUsernames.Contains(m.Username);

                var notification = BuildNotification(message, chat, isContact, isMentioned ? NotificationType.Mention : NotificationType.Message);

                await hubNotifier.SendToUserAsync(m.UserId, HubMethods.Chat.ReceiveNotification, notification);
            }
        }
        catch { /* Уведомления не должны ронять основной флоу */ }
    }

    private NotificationDto BuildNotification(MessageDto message, Domain.Entities.Chat? chat, bool isContact, NotificationType type)
    {
        var notificationType = type == NotificationType.Mention ? "mention" : message.Poll != null ? "poll" : "message";

        var preView = type == NotificationType.Mention ? $"Вас упомянули: {Truncate(message.Content, 100)}" : Truncate(message.Content, 100);

        return new NotificationDto
        {
            Type = notificationType,
            ChatId = message.ChatId,
            ChatName = isContact ? message.SenderName : chat?.Name,
            ChatAvatar = isContact ? message.SenderAvatarUrl : AvatarUrlHelper.BuildChatAvatarUrl(urlBuilder, chat?.Id ?? message.ChatId, chat?.Avatar),
            MessageId = message.Id,
            SenderId = message.SenderId,
            SenderName = message.SenderName,
            SenderAvatar = message.SenderAvatarUrl,
            PreView = preView,
            CreatedAt = message.CreatedAt
        };
    }

    private enum NotificationType { Message, Mention }

    private static HashSet<string> ExtractMentions(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MentionRegex().Matches(content))
        {
            var username = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(username))
                result.Add(username);
        }

        return result;
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    #endregion
}