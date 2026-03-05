namespace MessengerAPI.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Время жизни access token в минутах. По умолчанию 15 минут.
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    /// <summary>
    /// Время жизни refresh token в днях. По умолчанию 30 дней.
    /// </summary>
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    /// <summary>
    /// Устаревшее. Оставлено для обратной совместимости конфигурации.
    /// Используйте AccessTokenLifetimeMinutes.
    /// </summary>
    [Obsolete("Use AccessTokenLifetimeMinutes instead")]
    public int LifetimeHours { get; set; } = 24;

    public string Issuer { get; set; } = "MessengerAPI";
    public string Audience { get; set; } = "MessengerClient";
}