using Shared.Enum;

namespace Shared.Infrastructure;

public static class SystemEventMeta
{
    private record Meta(string Prefix, string Suffix = "", bool HasTarget = false);

    private static readonly Dictionary<SystemEventType, Meta> Map = new()
    {
        [SystemEventType.ChatCreated] = new(" создал(а) группу"),
        [SystemEventType.MemberAdded] = new(" добавил(а) ", " в группу", HasTarget: true),
        [SystemEventType.MemberRemoved] = new(" удалил(а) ", " из группы", HasTarget: true),
        [SystemEventType.MemberLeft] = new(" покинул(а) группу"),
        [SystemEventType.RoleChanged] = new(" изменил(а) роль ", HasTarget: true),
        [SystemEventType.CallStarted] = new(" начал(а) звонок"),
        [SystemEventType.CallEnded] = new(" завершил(а) звонок"),
        [SystemEventType.MessagePinned] = new(" закрепил(а) сообщение"),
        [SystemEventType.MessageUnpinned] = new(" открепил(а) сообщение"),
        [SystemEventType.ChatAvatarUpdated] = new(" обновил(а) фотографию группы"),
    };

    public static string GetPrefix(SystemEventType? type) =>
        type.HasValue && Map.TryGetValue(type.Value, out var m) ? m.Prefix : string.Empty;

    public static string GetSuffix(SystemEventType? type) =>
        type.HasValue && Map.TryGetValue(type.Value, out var m) ? m.Suffix : string.Empty;

    public static string Format(SystemEventType? type, string actor, string target)
    {
        if (!type.HasValue || !Map.TryGetValue(type.Value, out var m))
            return string.Empty;

        return m.HasTarget ? $"{actor}{m.Prefix}{target}{m.Suffix}" : $"{actor}{m.Prefix}";
    }
}