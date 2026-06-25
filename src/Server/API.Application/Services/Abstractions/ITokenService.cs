using API.Domain.Common;
using API.Domain.Entities;
using Microsoft.IdentityModel.Tokens;
using Shared.Enum;
using System.Security.Claims;

namespace API.Application.Services.Abstractions;

public interface ITokenService
{
    TokenPair GenerateTokenPair(int userId, UserRole? role = null);
    bool ValidateToken(string token, out int userId);
    Result<ClaimsPrincipal> GetPrincipalFromExpiredToken(string token);
    string HashToken(string token);
    TokenValidationParameters GetValidationParameters();
}
