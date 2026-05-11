using Shared.Helpers;
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

    /// <summary>
    /// Строит текст превью для панели ответа и пузыря цитаты.
    /// Аналог BuildPreview, но для MessageReplyPreviewDto.
    /// </summary>
    public static string BuildReplyPreview(MessageReplyPreviewDto reply)
    {
        if (reply.IsDeleted)
            return "Сообщение удалено";

        if (reply.IsVoiceMessage)
            return "Голосовое сообщение";

        if (reply.HasPoll)
            return "📊 " + BuildContentPreview(reply.Content, "Опрос");

        if (reply.FilesCount > 0 && string.IsNullOrWhiteSpace(reply.Content))
        {
            return reply.FilesCount == 1 ? "Вложение" : $"{reply.FilesCount} {Pluralize(reply.FilesCount, "файл", "файла", "файлов")}";
        }

        if (reply.FilesCount > 0 && !string.IsNullOrWhiteSpace(reply.Content))
            return $"{BuildContentPreview(reply.Content)}";

        return BuildContentPreview(reply.Content, "Сообщение");
    }

    public static string Pluralize(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod100 is >= 11 and <= 19) return many;
        return mod10 switch { 1 => one, 2 or 3 or 4 => few, _ => many };
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

        return SystemEventMeta.Format(message.SystemEventType, actorName, targetName);
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