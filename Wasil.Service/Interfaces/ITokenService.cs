using System.Security.Claims;
using Wasil.Data.Entities;

namespace Wasil.Service.Interfaces;

public interface ITokenService
{
    string GenerateToken(User user);
    RefreshToken GenerateRefreshToken(Guid userId);
    ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
}