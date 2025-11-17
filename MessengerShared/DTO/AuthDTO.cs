using System.ComponentModel.DataAnnotations;

namespace MessengerShared.DTO
{
    public class LoginDTO
    {
        [Required(ErrorMessage = "Username is required")]
        [MinLength(3, ErrorMessage = "Username must be at least 3 characters long")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [MinLength(3, ErrorMessage = "Password must be at least 3 characters long")]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterDTO
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public string? DisplayName { get; set; }
    }

    public class AuthResponseDTO
    {
        public int Id { get; set; }
        public string Username { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string Token { get; set; } = null!;
    }
}