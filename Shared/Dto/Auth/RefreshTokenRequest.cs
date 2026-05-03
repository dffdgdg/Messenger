namespace Shared.Dto.Auth;

public record RefreshTokenRequest(string AccessToken, string RefreshToken);