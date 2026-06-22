using Shared.Enum;

namespace Shared.Dto.Auth;

public class TokenResponseDto
{
    public string Token { get; set; } = null!;
    public int UserId { get; set; }
    public UserRole Role { get; set; }
}