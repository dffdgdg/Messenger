using MessengerAPI.Helpers;
using MessengerAPI.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace MessengerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController(MessengerDbContext context) : ControllerBase
{
    private readonly MessengerDbContext _context = context;

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            Console.WriteLine($"Login attempt for username: {request.Username}");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (user == null)
            {
                Console.WriteLine("User not found");
                return Unauthorized("Пользователь не найден");
            }

            Console.WriteLine($"User found: Id={user.Id}, Username={user.Username}");

            using var hmac = new HMACSHA256(Convert.FromBase64String(user.PasswordSalt));
            var hash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(request.Password)));

            if (hash != user.PasswordHash)
            {
                Console.WriteLine("Invalid password");
                return Unauthorized("Неверный пароль");
            }

            var token = TokenHelper.GenerateToken(user.Id);
            Console.WriteLine($"Login successful: UserId={user.Id}, Token generated");

            return Ok(new
            {
                user.Id,
                user.Username,
                user.DisplayName,
                Token = token
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Login error: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Имя и пароль обязательны");

        if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            return Conflict("Пользователь с таким именем уже существует");

        using var hmac = new HMACSHA256();
        var salt = Convert.ToBase64String(hmac.Key);
        var hash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(request.Password)));

        var user = new User
        {
            Username = request.Username,
            DisplayName = request.DisplayName,
            PasswordSalt = salt,
            PasswordHash = hash,
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = TokenHelper.GenerateToken(user.Id);

        return Ok(new
        {
            user.Id,
            user.Username,
            user.DisplayName,
            Token = token
        });
    }
}

public class LoginRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

public class RegisterRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public string? DisplayName { get; set; }
}