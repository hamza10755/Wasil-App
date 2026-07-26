using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Order : BaseEntity
{

    public int? StoreId { get; set; }

    public int? CustomerId { get; set; }

    public int? DriverId { get; set; }

    public string? OrderStatus { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Driver? Driver { get; set; }

    public virtual ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();

    public virtual Store? Store { get; set; }
}
