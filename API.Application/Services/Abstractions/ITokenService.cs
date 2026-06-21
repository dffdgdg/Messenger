using API.Domain.Common;
using API.Domain.Entities;
using Microsoft.IdentityModel.Tokens;
using Shared.Enum;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace API.Application.Services.Abstractions;

public interface ITokenService
{
    TokenPair GenerateTokenPair(int userId, UserRole? role = null);
    bool ValidateToken(string token, out int userId);
    Result<ClaimsPrincipal> GetPrincipalFromExpiredToken(string token);
    static string HashToken(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    TokenValidationParameters GetValidationParameters();
}