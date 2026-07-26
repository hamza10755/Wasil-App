using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class OrderLine : BaseEntity
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }

    public string ProductName { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }

    public string? ItemNote { get; set; }

    public virtual Order Order { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
    public virtual ICollection<OrderLineModifier> OrderLineModifiers { get; set; } = new List<OrderLineModifier>();
}