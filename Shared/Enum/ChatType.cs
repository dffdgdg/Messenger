using System.Runtime.Serialization;

namespace Shared.Enum;

public enum ChatType { Chat, Department, Contact, [EnumMember(Value = "department_heads")] DepartmentHeads }