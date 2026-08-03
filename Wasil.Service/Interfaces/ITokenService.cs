using Wasil.Data.Entities;

namespace Wasil.Service.Interfaces;

public interface ITokenService
{
    string GenerateToken(User user);
}