using System;
using System.Collections.Generic;

namespace Wasil.Service.DTOs;

public class OrderDetailDto
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string PaymentMethod { get; set; } = null!;
    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public string CustomerName { get; set; } = null!;
    public string StoreName { get; set; } = null!;
    public string? DeliveryAddress { get; set; }

    public IEnumerable<OrderLineDetailDto> Lines { get; set; } = new List<OrderLineDetailDto>();
    public IEnumerable<OrderStatusHistoryDto> StatusHistory { get; set; } = new List<OrderStatusHistoryDto>();
}
