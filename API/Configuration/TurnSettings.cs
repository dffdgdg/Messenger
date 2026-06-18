namespace API.Configuration;

public sealed class TurnSettings
{
    public const string Section = "TurnSettings";

    /// <summary>Включить TURN. false = только STUN (текущее поведение)</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>TURN сервер хост</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>TURN UDP порт</summary>
    public int Port { get; set; } = 3478;

    /// <summary>TURN TLS порт (опционально)</summary>
    public int TlsPort { get; set; } = 5349;

    /// <summary>Shared secret для временных credentials (RFC 8489)</summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>Время жизни credentials в секундах</summary>
    public int CredentialTtlSeconds { get; set; } = 86400; // 24 часа
}