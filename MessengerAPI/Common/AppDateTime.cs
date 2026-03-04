namespace MessengerAPI.Common;

/// <summary>
/// Единый источник времени.
/// EnableLegacyTimestampBehavior=true → Npgsql принимает Unspecified Kind.
/// Храним UTC, но без Kind чтобы Npgsql не ругался.
/// </summary>
public sealed class AppDateTime(TimeProvider timeProvider)
{
    public DateTime UtcNow => DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
}