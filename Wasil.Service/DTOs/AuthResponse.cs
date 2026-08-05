namespace Wasil.Api.DTOs.Auth;

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;    
    public string RefreshToken { get; set; } = string.Empty;
}