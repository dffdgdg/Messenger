namespace MessengerAPI.Data;

public sealed class TokenPair
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required string JwtId { get; init; }
}