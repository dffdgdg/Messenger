using System.Runtime.Serialization;

namespace MessengerShared.Enum;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemEventType
{
    [EnumMember(Value = "chat_created")] ChatCreated,
    [EnumMember(Value = "member_added")] MemberAdded,
    [EnumMember(Value = "member_removed")] MemberRemoved,
    [EnumMember(Value = "member_left")] MemberLeft,
    [EnumMember(Value = "role_changed")] RoleChanged
}