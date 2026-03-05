using MessengerShared.Enum;

namespace MessengerShared.Dto.Auth;

public class TokenResponseDto
{
    public string Token { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public int UserId { get; set; }
    public UserRole Role { get; set; }
}