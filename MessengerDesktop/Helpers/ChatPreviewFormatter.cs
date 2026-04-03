namespace MessengerDesktop.Helpers;

public static class ChatPreviewFormatter
{
    private const int ContentPreviewMaxLength = 100;
    private const string PreviewEllipsis = "...";

    public static string? FormatSenderName(string? senderName, int? senderId, int? currentUserId)
    {
        if (senderId.HasValue && currentUserId.HasValue && senderId.Value == currentUserId.Value)
            return "Вы";

        if (string.IsNullOrWhiteSpace(senderName))
            return null;

        return senderName;
    }

    public static string BuildPreview(MessageDto message, int? currentUserId = null)
    {
        if (message.Poll != null) return "Опрос";
        if (message.IsVoiceMessage) return "Голосовое сообщение";
        if (message.IsSystemMessage) return BuildSystemPreview(message, currentUserId);
        if (message.Files.Count > 0 && string.IsNullOrWhiteSpace(message.Content))
            return "Вложение";

        return BuildContentPreview(message.Content);
    }

    private static string BuildSystemPreview(MessageDto message, int? currentUserId)
    {
        var actorName = FormatParticipantName(message.SenderName, message.SenderId, currentUserId, "Пользователь");
        var targetName = FormatParticipantName(message.TargetUserName, message.TargetUserId, currentUserId, "пользователя");

        return message.SystemEventType switch
        {
            SystemEventType.ChatCreated => $"{actorName} создал(а) группу",
            SystemEventType.MemberAdded => $"{actorName} добавил(а) {targetName} в группу",
            SystemEventType.MemberRemoved => $"{actorName} удалил(а) {targetName} из группы",
            SystemEventType.MemberLeft => $"{actorName} покинул(а) группу",
            SystemEventType.RoleChanged => $"{actorName} изменил(а) роль участника {targetName}",
            _ => BuildContentPreview(message.Content)
        };
    }

    private static string FormatParticipantName(string? name, int? participantId, int? currentUserId, string fallback)
    {
        var formatted = FormatSenderName(name, participantId, currentUserId);
        return string.IsNullOrWhiteSpace(formatted) ? fallback : formatted;
    }

    public static string BuildContentPreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "Сообщение";

        return content.Length > ContentPreviewMaxLength ? content[..ContentPreviewMaxLength] + PreviewEllipsis : content;
    }
}
