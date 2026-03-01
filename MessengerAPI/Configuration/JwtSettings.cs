namespace MessengerAPI.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Secret { get; set; } = string.Empty;
    public int LifetimeHours { get; set; } = 24;
    public string Issuer { get; set; } = "MessengerAPI";
    public string Audience { get; set; } = "MessengerClient";
}