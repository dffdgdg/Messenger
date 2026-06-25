namespace Shared.Contracts.Call;

/// <summary>
/// Универсальный конверт для сигналов (Offer / Answer / ICECandidate)
/// </summary>
public class SignalDto
{
    public string CallId { get; set; } = string.Empty;

    /// <summary>От кого пришёл сигнал (заполняется сервером)</summary>
    public int FromUserId { get; set; }

    /// <summary>Кому адресован сигнал</summary>
    public int TargetUserId { get; set; }

    /// <summary>offer | answer | ice-candidate</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>SDP или JSON-сериализованный ICE candidate</summary>
    public string Payload { get; set; } = string.Empty;
}