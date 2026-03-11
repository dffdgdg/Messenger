using NpgsqlTypes;

namespace MessengerShared.Enum;

public enum TranscriptionStatus
{
    [PgName("pending")]
    Pending,

    [PgName("processing")]
    Processing,

    [PgName("done")]
    Done,

    [PgName("failed")]
    Failed
}