namespace Wasil.Service.DTOs;

public class UpdateCustomerDto
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public string? Gender { get; set; }
    public string? Location { get; set; }
}
