namespace MessengerAPI.Common;

/// <summary>
/// Единый источник времени.
/// EnableLegacyTimestampBehavior=true → Npgsql принимает Unspecified Kind.
/// Храним UTC, но без Kind чтобы Npgsql не ругался.
/// </summary>
public static class AppDateTime
{
    public static DateTime UtcNow => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}