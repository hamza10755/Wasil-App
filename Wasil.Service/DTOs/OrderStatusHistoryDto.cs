using System;

namespace Wasil.Service.DTOs;

public class OrderStatusHistoryDto
{
    public string OldStatus { get; set; } = null!;
    public string NewStatus { get; set; } = null!;
    public DateTime TimestampUtc { get; set; }
}
