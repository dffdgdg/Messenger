using System;

namespace Desktop.Infrastructure.Helpers;

public static class ChatPreviewFormatter
{
    private const int ContentPreviewMaxLength = 100;
    private const string PreviewEllipsis = "...";

    public static string? FormatSenderName(string? senderName, int? senderId, int? currentUserId)
    {
        if (senderId.HasValue && currentUserId.HasValue && senderId.Value == currentUserId.Value)
            return "Вы";

        if (string.IsNullOrWhiteSpace(senderName)) return null;

        var parts = senderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^1];
    }

    public static (string Preview, bool HidePrefix) BuildPreviewWithMeta(MessageDto message, int? currentUserId = null, bool isDialog = false)
    {
        if (message.IsDeleted)
            return ("Сообщение удалено", HidePrefix: true);

        if (message.IsSystemMessage)
            return (BuildSystemPreview(message, currentUserId), HidePrefix: true);

        string preview;
        bool hidePrefix = isDialog;

        if (message.Poll != null)
        {
            var question = BuildContentPreview(message.Content, "Опрос");
            preview = $"📊 {question}";
        }
        else if (message.IsVoiceMessage)
        {
            preview = "Голосовое сообщение";
        }
        else if (message.Files.Count > 0 && string.IsNullOrWhiteSpace(message.Content))
        {
            preview = "Вложение";
        }
        else
        {
            preview = BuildContentPreview(message.Content);
        }

        return (preview, hidePrefix);
    }

    public static string BuildPreview(MessageDto message, int? currentUserId = null, bool isDialog = false)
        => BuildPreviewWithMeta(message, currentUserId, isDialog).Preview;

    private static string BuildSystemPreview(MessageDto message, int? currentUserId)
    {
        var actorName = FormatParticipantName(message.SenderName, message.SenderId, currentUserId, "Пользователь");
        var targetName = FormatParticipantName(message.TargetUserName, message.TargetUserId, currentUserId, "пользователя");

        return message.SystemEventType switch
        {
            SystemEventType.ChatCreated => $"{actorName} создал(а) группу",
            SystemEventType.MemberAdded => $"{actorName} добавил(а) {targetName}",
            SystemEventType.MemberRemoved => $"{actorName} удалил(а) {targetName}",
            SystemEventType.MemberLeft => $"{actorName} покинул(а) группу",
            SystemEventType.RoleChanged => $"{actorName} изменил(а) роль {targetName}",
            SystemEventType.CallStarted => $"{actorName} начал(а) звонок",
            SystemEventType.CallEnded => BuildContentPreview(message.Content, "Звонок завершён"),
            _ => BuildContentPreview(message.Content)
        };
    }

    private static string FormatParticipantName(string? name, int? id, int? currentUserId, string fallback)
    {
        var formatted = FormatSenderName(name, id, currentUserId);
        return string.IsNullOrWhiteSpace(formatted) ? fallback : formatted;
    }

    public static string BuildContentPreview(string? content, string fallback = "Сообщение")
    {
        if (string.IsNullOrWhiteSpace(content)) return fallback;
        return content.Length > ContentPreviewMaxLength ? content[..ContentPreviewMaxLength] + PreviewEllipsis : content;
    }
}