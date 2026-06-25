namespace Shared.Contracts.Call;

public class RelayEndpointInfo
{
    // Существующие поля — не трогаем (используются для UDP relay в ServerMixed)
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string CallId { get; set; } = string.Empty;

    // Новые поля для TURN (только для PeerToPeer)
    public TurnCredentials? Turn { get; set; }
}

public class TurnCredentials
{
    public string[] Urls { get; set; } = [];
    public string Username { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
    /// <summary>Unix timestamp — до которого валидны credentials</summary>
    public long ExpiresAt { get; set; }
}