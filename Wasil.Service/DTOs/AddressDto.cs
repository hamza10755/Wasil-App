namespace Wasil.Service.DTOs;

public class AddressDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Street { get; set; } = null!;
    public string City { get; set; } = null!;
    public string ZipCode { get; set; } = null!;
}
