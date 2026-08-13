using System;

namespace Wasil.Data.Entities;

public class DailyReport : BaseEntity
{
    public int StoreId { get; set; }
    public virtual Store Store { get; set; } = null!;
    public DateTime ReportDate { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public string TopSellingProductName { get; set; } = string.Empty;
}
