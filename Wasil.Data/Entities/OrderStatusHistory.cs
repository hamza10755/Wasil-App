using System;

namespace Wasil.Data.Entities;

public partial class OrderStatusHistory : BaseEntity
{
    public int OrderId { get; set; }
    public string OldStatus { get; set; } = null!;
    public string NewStatus { get; set; } = null!;
    public DateTime TimestampUtc { get; set; }

    public virtual Order Order { get; set; } = null!;
}
