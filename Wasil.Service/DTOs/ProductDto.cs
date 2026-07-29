namespace Wasil.Service.DTOs;

public class ProductDto
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool Availability { get; set; }
}
