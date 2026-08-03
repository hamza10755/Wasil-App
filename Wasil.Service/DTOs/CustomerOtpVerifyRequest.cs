namespace Wasil.Api.DTOs.Auth;

public class CustomerOtpVerifyRequest
{
    public string Phone { get; set; } = string.Empty;
    
    public string Code { get; set; } = string.Empty; 
}