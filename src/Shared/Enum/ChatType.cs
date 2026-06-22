using System.Runtime.Serialization;

namespace Shared.Enum;

public enum ChatType
{
    [EnumMember(Value = "chat")]
    Chat,

    [EnumMember(Value = "department")]
    Department,

    [EnumMember(Value = "contact")]
    Contact,

    [EnumMember(Value = "department_heads")]
    DepartmentHeads
}