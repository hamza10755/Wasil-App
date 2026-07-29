using System.Collections.Generic;
using Wasil.Data.Enums;

namespace Wasil.Service.DTOs;

public class CreateOrderDto
{
    public int CustomerId { get; set; }
    public int StoreId { get; set; }
    public int AddressId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public List<CreateOrderLineDto> Lines { get; set; } = new();
}

public class CreateOrderLineDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}