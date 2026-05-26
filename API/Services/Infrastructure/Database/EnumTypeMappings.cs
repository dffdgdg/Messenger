using API.Data;
using Shared.Enum;

namespace API.Services.Infrastructure.Database;

public static class EnumTypeMappings
{
    public static EnumNameTranslator ChatRoleNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(ChatRole.Member)] = "member",
        [nameof(ChatRole.Admin)] = "admin",
        [nameof(ChatRole.Owner)] = "owner"
    });

    public static EnumNameTranslator ChatTypeNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(ChatType.Chat)] = "chat",
        [nameof(ChatType.Department)] = "department",
        [nameof(ChatType.Contact)] = "contact",
        [nameof(ChatType.DepartmentHeads)] = "department_heads"
    });

    public static EnumNameTranslator SystemEventTypeNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(SystemEventType.ChatCreated)] = "chat_created",
        [nameof(SystemEventType.MemberAdded)] = "member_added",
        [nameof(SystemEventType.MemberRemoved)] = "member_removed",
        [nameof(SystemEventType.MemberLeft)] = "member_left",
        [nameof(SystemEventType.RoleChanged)] = "role_changed",
        [nameof(SystemEventType.CallStarted)] = "call_started",
        [nameof(SystemEventType.CallEnded)] = "call_ended",
        [nameof(SystemEventType.MessagePinned)] = "message_pinned",
        [nameof(SystemEventType.MessageUnpinned)] = "message_unpinned",
        [nameof(SystemEventType.ChatAvatarUpdated)] = "chat_avatar_updated"
    });

    public static EnumNameTranslator ThemeNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(Theme.light)] = "light",
        [nameof(Theme.dark)] = "dark",
        [nameof(Theme.system)] = "system"
    });

    public static EnumNameTranslator UserStatusTypeNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(UserStatusType.Online)] = "online",
        [nameof(UserStatusType.Away)] = "away",
        [nameof(UserStatusType.DoNotDisturb)] = "do_not_disturb",
        [nameof(UserStatusType.Busy)] = "busy"
    });
}