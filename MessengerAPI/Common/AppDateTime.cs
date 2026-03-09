namespace MessengerAPI.Common;

/// <summary>
/// Единый источник времени. Возвращает DateTime без Kind
/// для совместимости с PostgreSQL "timestamp without time zone".
/// </summary>
public sealed class AppDateTime(TimeProvider timeProvider)
{
    public DateTime UtcNow => DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
}