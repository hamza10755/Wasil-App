using System;

namespace Wasil.Service.DTOs;

public class CustomerOrderHistoryDto
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = null!;
    public DateTime Date { get; set; }
    public string Status { get; set; } = null!;
    public string StoreName { get; set; } = null!;
    public int LineCount { get; set; }
    public decimal Total { get; set; }
}
