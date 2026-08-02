namespace Wasil.Service.DTOs;

public class CreateAddressDto
{
    public string Street { get; set; } = null!;
    public string City { get; set; } = null!;
    public string ZipCode { get; set; } = null!;
}
