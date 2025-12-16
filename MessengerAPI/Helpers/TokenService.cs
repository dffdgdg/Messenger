using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MessengerAPI.Helpers
{
    public interface ITokenService
    {
        string GenerateToken(int userId, string? role = null);
        bool ValidateToken(string token, out int userId);
    }

    public class TokenService(IConfiguration config) : ITokenService
    {
        private readonly string _secretKey = config["Jwt:Secret"] ?? throw new ArgumentNullException("Jwt:Secret not found");
        private readonly int _lifetimeHours = int.TryParse(config["Jwt:LifetimeHours"], out var hours) ? hours : 24;
        private readonly string _issuer = config["Jwt:Issuer"] ?? "MessengerAPI";
        private readonly string _audience = config["Jwt:Audience"] ?? "MessengerClient";

        public string GenerateToken(int userId, string? role = null)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.Iat, DateTimeOffset.Now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };

            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.Now.AddHours(_lifetimeHours),
                Issuer = _issuer,
                Audience = _audience,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public bool ValidateToken(string token, out int userId)
        {
            userId = 0;
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var parameters = GetValidationParameters();

                var principal = handler.ValidateToken(token, parameters, out _);
                var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
                return int.TryParse(idClaim?.Value, out userId);
            }
            catch
            {
                return false;
            }
        }

        public TokenValidationParameters GetValidationParameters()
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));

            return new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        }

        public static TokenValidationParameters GetValidationParameters(IConfiguration config)
        {
            var secret = config["Jwt:Secret"] ?? throw new ArgumentNullException("Jwt:Secret");
            var issuer = config["Jwt:Issuer"] ?? "MessengerAPI";
            var audience = config["Jwt:Audience"] ?? "MessengerClient";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            return new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        }
    }
}