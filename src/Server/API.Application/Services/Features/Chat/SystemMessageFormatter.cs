using Shared.Enum;
using Shared.Infrastructure;

namespace API.Application.Services.Features.Chat;

public static class SystemMessageFormatter
{
    private const string DefaultActor = "Пользователь";
    private const string DefaultTarget = "пользователя";

    public static string Format(SystemEventType? eventType, string? senderName, string? targetName)
    {
        var actor = string.IsNullOrWhiteSpace(senderName) ? DefaultActor : senderName;
        var target = string.IsNullOrWhiteSpace(targetName) ? DefaultTarget : targetName;

        return SystemEventMeta.Format(eventType, actor, target);
    }
}
