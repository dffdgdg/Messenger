using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MessengerAPI.Helpers
{
    public static class TokenHelper
    {
        private static readonly string SecretKey = "vRQHb2XkyCqD7hZP9xjMwN5tF3gAS4Ue";

        public static TokenValidationParameters GetValidationParameters()
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
            return new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        }

        public static string GenerateToken(int userId)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
                };

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.UtcNow.AddDays(1),
                    SigningCredentials = new SigningCredentials(
                        key,
                        SecurityAlgorithms.HmacSha256Signature)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                return tokenHandler.WriteToken(token);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Ошибка при генерации токена", ex);
            }
        }

        public static bool ValidateToken(string token, out int userId)
        {
            userId = 0;

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = GetValidationParameters();

                var claimsPrincipal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
                
                if (validatedToken is not JwtSecurityToken jwtToken)
                {
                    return false;
                }

                if (!jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256Signature, 
                    StringComparison.InvariantCultureIgnoreCase))
                {
                    return false;
                }

                var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out userId))
                {
                    return false;
                }

                var expirationTime = validatedToken.ValidTo;
                if (expirationTime <= DateTime.UtcNow)
                    return false;
                return true;
            }
            catch { return false; }
        }
    }
}