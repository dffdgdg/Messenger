using MessengerShared.Enum;

namespace MessengerShared.DTO.Auth;

public class AuthResponseDTO
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Token { get; set; } = null!;
    public UserRole Role { get; set; }
}