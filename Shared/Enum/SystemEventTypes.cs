using System.Runtime.Serialization;

namespace Shared.Enum;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemEventType
{
    [EnumMember(Value = "chat_created")] ChatCreated,
    [EnumMember(Value = "member_added")] MemberAdded,
    [EnumMember(Value = "member_removed")] MemberRemoved,
    [EnumMember(Value = "member_left")] MemberLeft,
    [EnumMember(Value = "role_changed")] RoleChanged,
    [EnumMember(Value = "call_started")] CallStarted,
    [EnumMember(Value = "call_ended")] CallEnded,
    [EnumMember(Value = "message_pinned")] MessagePinned,
    [EnumMember(Value = "message_unpinned")] MessageUnpinned,
}