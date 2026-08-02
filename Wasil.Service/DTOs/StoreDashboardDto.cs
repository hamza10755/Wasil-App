using System.Collections.Generic;

namespace Wasil.Service.DTOs;

public class StoreDashboardDto
{
    public int TodayOrderCount { get; set; }
    public decimal TodayRevenue { get; set; }
    public decimal AvgOrderValueLast30Days { get; set; }
    public List<TopProductDto> TopProductsLast30Days { get; set; } = new();
    public Dictionary<string, int> ActiveStatusCounts { get; set; } = new();
}

public class TopProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public int QuantitySold { get; set; }
}
