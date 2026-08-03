namespace Wasil.Api.DTOs.Auth;

public class StaffLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}