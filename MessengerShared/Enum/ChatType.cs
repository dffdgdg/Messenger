using System.Runtime.Serialization;

namespace MessengerShared.Enum;

public enum ChatType  { Chat, Department, Contact,[EnumMember(Value = "department_heads")]DepartmentHeads }