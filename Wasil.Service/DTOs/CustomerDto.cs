namespace Wasil.Service.DTOs;

public class CustomerDto
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Email { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string? Gender { get; set; }
    public string? Location { get; set; }
}
