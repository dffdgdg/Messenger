using Shared.Enum;

namespace Shared.Contracts.Auth;

public class TokenResponseDto
{
    public string Token { get; set; } = null!;
    public int UserId { get; set; }
    public UserRole Role { get; set; }
}