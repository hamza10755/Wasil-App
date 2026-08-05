using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Wasil.Data.Interfaces;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                        ?? User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            
            if (Guid.TryParse(value, out var guid)) return guid;
            return null;
        }
    }

    public string? Role => User?.FindFirst(ClaimTypes.Role)?.Value;

    public int? StoreId
    {
        get
        {
            var value = User?.FindFirst("storeId")?.Value;
            if (int.TryParse(value, out var id)) return id;
            return null;
        }
    }
}