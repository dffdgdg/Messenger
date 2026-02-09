using NpgsqlTypes;
using System.Runtime.Serialization;

namespace MessengerShared.Enum;

public enum ChatType
{
    [PgName("Chat")]
    Chat,
    [PgName("Department")]
    Department,
    [PgName("Contact")]
    Contact,
    [EnumMember(Value = "department_heads")]
    DepartmentHeads
}