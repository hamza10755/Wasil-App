using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class OrderLine : BaseEntity
{

    public int? Quantity { get; set; }

    public string? ItemNote { get; set; }

    public int? TotalPrice { get; set; }

    public int? OrderId { get; set; }

    public int? ProductId { get; set; }

    public virtual Order? Order { get; set; }

    public virtual ICollection<OrderLineModifier> OrderLineModifiers { get; set; } = new List<OrderLineModifier>();

    public virtual Product? Product { get; set; }
}
