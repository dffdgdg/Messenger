using Shared.Contracts.Auth;

namespace API.Application.Services.Core.Auth;

/// <summary>
/// Внутренний результат логина — содержит и DTO для клиента, и RefreshToken для cookie.
/// RefreshToken не попадает в JSON — только через SetCookie в контроллере.
/// </summary>
public sealed class AuthLoginResult
{
    public required AuthResponseDto Response { get; init; }
    public required string RefreshToken { get; init; }
}

/// <summary>
/// Внутренний результат refresh — содержит и DTO для клиента, и новый RefreshToken для cookie.
/// </summary>
public sealed class AuthRefreshResult
{
    public required TokenResponseDto Response { get; init; }
    public required string RefreshToken { get; init; }
}
