namespace API.Common;

public static class StatusExtensions
{
    public static TimeSpan? Parse(this string? duration) => duration switch
    {
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "2h" => TimeSpan.FromHours(2),
        "4h" => TimeSpan.FromHours(4),
        "8h" => TimeSpan.FromHours(8),
        "24h" => TimeSpan.FromHours(24),
        _ => null
    };
}