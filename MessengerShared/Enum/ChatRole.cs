using NpgsqlTypes;

namespace MessengerShared.Enum;

public enum ChatRole
{
    [PgName("member")]
    Member,

    [PgName("admin")]
    Admin,

    [PgName("owner")]
    Owner
}