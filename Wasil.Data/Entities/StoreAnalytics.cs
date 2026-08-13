using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public class StoreAnalytics
{
    public long Id { get; set; }
    public long StoreId { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}