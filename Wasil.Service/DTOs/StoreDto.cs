namespace Wasil.Service.DTOs;

public class StoreDto
{
    public int Id { get; set; }
    public string StoreName { get; set; } = null!;
    public string StoreLocation { get; set; } = null!;
    public bool Status { get; set; }
}
