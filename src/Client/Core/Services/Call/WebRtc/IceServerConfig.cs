using Shared.Contracts.Call;

namespace Core.Services.Call.WebRtc;

/// <summary>
/// Конфигурация ICE серверов — общая для всех платформ.
/// Вынесена из класса чтобы быть доступной под Android.
/// </summary>
public sealed class IceServerConfig
{
    public string[]? StunUrls { get; init; }
    public TurnCredentials? Turn { get; init; }
    public bool TurnOnly { get; init; } = false;

    public static IceServerConfig FromRelayEndpoint(RelayEndpointInfo info)
    {
        return new IceServerConfig
        {
            Turn = info.Turn,
            StunUrls = info.Turn == null
                ? ["stun:stun.l.google.com:19302"]
                : null
        };
    }
}