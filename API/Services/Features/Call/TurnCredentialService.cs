using System.Security.Cryptography;
using System.Text;

namespace API.Services.Features.Call;

/// <summary>
/// Генерирует временные TURN credentials по схеме RFC 8489 (time-limited credentials).
/// Username = "{timestamp}:{userId}", credential = HMAC-SHA1(sharedSecret, username).
/// Coturn поддерживает это из коробки через use-auth-secret.
/// </summary>
public sealed class TurnCredentialService(
    IOptions<TurnSettings> options,
    ILogger<TurnCredentialService> logger)
{
    private readonly TurnSettings _settings = options.Value;

    public TurnCredentials? GenerateCredentials(int userId)
    {
        if (!_settings.Enabled)
            return null;

        if (string.IsNullOrWhiteSpace(_settings.SharedSecret))
        {
            logger.LogWarning(
                "[TURN] SharedSecret не настроен. TURN credentials не будут выданы.");
            return null;
        }

        // Время истечения = now + TTL
        var expiresAt = DateTimeOffset.UtcNow
            .AddSeconds(_settings.CredentialTtlSeconds)
            .ToUnixTimeSeconds();

        // Username в формате который понимает coturn: "{expires}:{userId}"
        var username = $"{expiresAt}:{userId}";

        // HMAC-SHA1 по shared secret
        var credential = ComputeHmacSha1(_settings.SharedSecret, username);

        var urls = BuildTurnUrls();

        logger.LogDebug("[TURN] Выданы credentials для userId={UserId}, expires={ExpiresAt}", userId, expiresAt);

        return new TurnCredentials
        {
            Urls = urls,
            Username = username,
            Credential = credential,
            ExpiresAt = expiresAt
        };
    }

    private string[] BuildTurnUrls()
    {
        var host = _settings.Host;
        var urls = new List<string>
        {
            // UDP (основной)
            $"turn:{host}:{_settings.Port}",
            // TCP (fallback когда UDP заблокирован)
            $"turn:{host}:{_settings.Port}?transport=tcp",
        };

        // TLS только если порт настроен и отличается от основного
        if (_settings.TlsPort > 0 && _settings.TlsPort != _settings.Port)
            urls.Add($"turns:{host}:{_settings.TlsPort}?transport=tcp");

        return [.. urls];
    }

    private static string ComputeHmacSha1(string secret, string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }
}