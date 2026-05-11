namespace API.Services.Features.Chat;

public static class SystemMessageFormatter
{
    private const string DefaultActor = "Пользователь";
    private const string DefaultTarget = "пользователя";
    private const string DefaultSystemMessage = "Системное сообщение";

    public static string Format(SystemEventType? eventType, string? senderName, string? targetName, string? fallback = null)
    {
        var actor = string.IsNullOrWhiteSpace(senderName) ? DefaultActor : senderName;
        var target = string.IsNullOrWhiteSpace(targetName) ? DefaultTarget : targetName;

        return eventType switch
        {
            SystemEventType.ChatCreated => $"{actor} создал(а) группу",
            SystemEventType.MemberAdded => $"{actor} добавил(а) {target}",
            SystemEventType.MemberRemoved => $"{actor} удалил(а) {target}",
            SystemEventType.MemberLeft => $"{actor} покинул(а) группу",
            SystemEventType.RoleChanged => $"{actor} изменил(а) роль {target}",
            SystemEventType.CallStarted => $"{actor} начал(а) звонок",
            SystemEventType.CallEnded => fallback ?? "Звонок завершён",
            SystemEventType.MessagePinned => $"{actor} закрепил(а) сообщение"
                + (string.IsNullOrWhiteSpace(fallback) ? "" : $": «{fallback}»"),
            SystemEventType.MessageUnpinned => $"{actor} открепил(а) сообщение",
            _ => fallback ?? DefaultSystemMessage
        };
    }
}