using NpgsqlTypes;

namespace MessengerShared.Enum
{
    public enum ChatType

    {
        [PgName("Chat")]
        Chat,
        [PgName("Department")]
        Department,
        [PgName("Contact")]
        Contact
    }
}
